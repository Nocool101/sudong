using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using QuickLauncher.App.Hooks;
using QuickLauncher.App.Scenes;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Storage;

namespace QuickLauncher.App.Interaction;

/// <summary>
/// 调试日志（写入 %TEMP%/ql_debug.log）
/// </summary>
public static class Dbg
{
    private static readonly string LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ql_debug.log");
    private static readonly object Lock = new();
    public static void Log(string msg)
    {
        lock (Lock)
        {
            try
            {
                System.IO.File.AppendAllText(LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}" + Environment.NewLine);
            }
            catch { }
        }
    }
}

/// <summary>
/// 交互状态机：把全局钩子事件转换为面板行为。
/// 核心交互：
/// - 单击触发键（中键/X1/X2/右键/左键）→ 开关面板
/// - 快击 Ctrl → 开关面板
/// - Esc → 关闭面板
/// </summary>
public sealed class InteractionController : IDisposable
{
    private enum State { Idle, MiddlePressed }

    private readonly GlobalHooks _hooks;
    private SceneMatcher _matcher;
    private readonly ActionStore _store;
    private readonly StepExecutor _executor;
    private readonly IPanelService _panel;
    private readonly AppSettings _settings;

    private State _state = State.Idle;
    private long _middleDownTick;
    private int _downX, _downY;
    /// <summary>单击判定的最大时长（ms）</summary>
    public int ClickThresholdMs { get; set; } = 350;

    /// <summary>
    /// 是否忽略合成（injected）输入事件。默认 false：真实鼠标/键盘与鼠标宏软件都能触发；
    /// 动作执行期间通过 Suppress() 防止自触发死循环。
    /// </summary>
    public bool IgnoreInjected { get; set; }

    private int _suppressDepth;

    /// <summary>Ctrl 按下的时间戳（0 = 未按下）；快击判定用</summary>
    private long _ctrlDownTick;

    /// <summary>Ctrl 按住期间是否按过其他键（组合操作则不视为快击）</summary>
    private bool _ctrlComboUsed;

    /// <summary>替换匹配器（App 层场景数据变更后调用）</summary>
    public void ReplaceMatcher(SceneMatcher matcher) => _matcher = matcher;

    /// <summary>进入抑制区（执行动作期间调用，防止自触发）</summary>
    public IDisposable Suppress()
    {
        _suppressDepth++;
        return new Suppressor(this);
    }

    private sealed class Suppressor : IDisposable
    {
        private readonly InteractionController _owner;
        public Suppressor(InteractionController owner) => _owner = owner;
        public void Dispose() => _owner._suppressDepth--;
    }

    private bool ShouldIgnore(GlobalHooks.MouseEventData e) => _suppressDepth > 0 || App.IsModalDialogOpen || (IgnoreInjected && e.Injected);
    private bool ShouldIgnore(GlobalHooks.KeyEventData e) => _suppressDepth > 0 || App.IsModalDialogOpen || (IgnoreInjected && e.Injected);

    public InteractionController(
        GlobalHooks hooks,
        SceneMatcher matcher,
        ActionStore store,
        StepExecutor executor,
        IPanelService panel,
        AppSettings settings)
    {
        _hooks = hooks;
        _matcher = matcher;
        _store = store;
        _executor = executor;
        _panel = panel;
        _settings = settings;

        _hooks.MouseDown += OnMouseDown;
        _hooks.MouseUp += OnMouseUp;
        _hooks.KeyDown += OnKeyDown;
        _hooks.KeyUp += OnKeyUp;
        _panel.PanelShown += OnPanelShown;
    }

    private long _panelShownTick;

    /// <summary>面板最小存活时间（ms）。防止侧键重复事件把刚弹出的面板立刻关掉。</summary>
    public int MinPanelAliveMs { get; set; } = 600;

    private void OnPanelShown()
    {
        _panelShownTick = Stopwatch.GetTimestamp();
    }

    #region 鼠标

    private void OnMouseDown(GlobalHooks.MouseEventData e)
    {
        Dbg.Log($"MouseDown btn={e.Button}");
        if (ShouldIgnore(e)) return;
        if (_panel.IsSuspended) return;
        // 磁贴拖放进行中：暂停一切面板关闭/触发，防止拖放途中面板消失
        if (_panel.IsInteracting) return;

        // 面板打开时：点击面板外部 → 关闭面板（刚打开/刷新 300ms 内忽略，防止位置突变误判）
        if (_panel.IsPanelOpen && e.Button == GlobalHooks.MouseButtons.Left && !IsInsidePanel(e.X, e.Y))
        {
            var openMs = _panelShownTick == 0
                ? double.MaxValue
                : (Stopwatch.GetTimestamp() - _panelShownTick) * 1000.0 / Stopwatch.Frequency;
            if (openMs < 300)
            {
                Dbg.Log($"outside click ignored (panel open {openMs:F0}ms)");
                return;
            }
            Dbg.Log($"outside click at ({e.X},{e.Y}) -> HidePanel");
            _panel.HidePanel();
            return;
        }

        // 面板打开时，面板内的左键按下 = UI 操作（点击磁贴/拖放/翻页），不记为触发键。
        // 否则拖放结束时松开的 MouseUp 会命中"单击触发"判定，把面板关掉（拖放失败）。
        if (_panel.IsPanelOpen && e.Button == GlobalHooks.MouseButtons.Left && IsInsidePanel(e.X, e.Y))
        {
            Dbg.Log("left click inside panel: treated as UI interaction");
            return;
        }

        var buttonName = ButtonName(e.Button);
        var panelButtons = _settings.PanelTrigger.EffectiveButtons.ToList();

        if (buttonName is not null && panelButtons.Contains(buttonName))
        {
            // 防抖：同按钮短时间内的重复按下忽略（部分软件/硬件会 reinject 侧键事件）
            var now = Stopwatch.GetTimestamp();
            var sinceLastMs = _lastTriggerButton == buttonName
                ? (now - _lastTriggerTick) * 1000.0 / Stopwatch.Frequency
                : double.MaxValue;
            if (sinceLastMs < TriggerDebounceMs)
            {
                Dbg.Log($"debounced {buttonName} (+{sinceLastMs:F0}ms)");
                return;
            }
            _lastTriggerButton = buttonName;
            _lastTriggerTick = now;

            _pressedButton = buttonName;
            _state = State.MiddlePressed;
            _middleDownTick = Stopwatch.GetTimestamp();
            _downX = e.X;
            _downY = e.Y;
        }
    }

    private string? _pressedButton;
    private string? _lastTriggerButton;
    private long _lastTriggerTick;

    /// <summary>同按钮重复触发防抖间隔（ms）。防止 reinject/硬件抖动导致"显示后立即关闭"。</summary>
    public int TriggerDebounceMs { get; set; } = 400;

    /// <summary>按钮枚举转名称</summary>
    private static string? ButtonName(GlobalHooks.MouseButtons button) => button switch
    {
        GlobalHooks.MouseButtons.Left => "Left",
        GlobalHooks.MouseButtons.Right => "Right",
        GlobalHooks.MouseButtons.Middle => "Middle",
        GlobalHooks.MouseButtons.X1 => "X1",
        GlobalHooks.MouseButtons.X2 => "X2",
        _ => null
    };

    private void OnMouseUp(GlobalHooks.MouseEventData e)
    {
        Dbg.Log($"MouseUp btn={e.Button} injected={e.Injected} state={_state}");
        if (ShouldIgnore(e)) return;

        var buttonName = ButtonName(e.Button);
        if (buttonName is not null && buttonName == _pressedButton)
        {
            if (_state == State.MiddlePressed)
            {
                // 单击判定 → 在按下点切换面板
                var elapsedMs = ElapsedMs(_middleDownTick);
                if (elapsedMs <= ClickThresholdMs)
                    TogglePanel(_downX, _downY);
                _state = State.Idle;
            }
        }
    }

    #endregion

    #region 键盘

    private void OnKeyDown(GlobalHooks.KeyEventData e)
    {
        if (ShouldIgnore(e)) return;
        if (_panel.IsInteracting) return;

        // Esc 取消
        if (e.VkCode == 0x1B)
        {
            if (_panel.IsPanelOpen)
                _panel.HidePanel();
            return;
        }

        // Ctrl 快击 → 面板：按下记录时间，抬起时判定（见 OnKeyUp）
        if (e.VkCode == 0x11)
        {
            _ctrlDownTick = Stopwatch.GetTimestamp();
            _ctrlComboUsed = false;
            return;
        }

        // Ctrl 按住期间按了其他任何键 → 组合操作，抬起时不再视为快击
        if (_ctrlDownTick != 0)
            _ctrlComboUsed = true;
    }

    private void OnKeyUp(GlobalHooks.KeyEventData e)
    {
        if (ShouldIgnore(e)) return;
        if (e.VkCode != 0x11 || _ctrlDownTick == 0) return;

        var elapsedMs = (Stopwatch.GetTimestamp() - _ctrlDownTick) * 1000.0 / Stopwatch.Frequency;
        _ctrlDownTick = 0;
        // 快击判定：按住时间短、且期间未按其他键。用当前光标位置呼出（跟随鼠标）
        if (!_ctrlComboUsed && elapsedMs <= ClickThresholdMs)
        {
            var (cx, cy) = CursorPosition();
            TogglePanel(cx, cy);
        }
    }

    #endregion

    #region 面板

    private void TogglePanel(int x, int y)
    {
        Dbg.Log($"TogglePanel: currentlyOpen={_panel.IsPanelOpen} at ({x},{y})");
        if (_panel.IsPanelOpen)
        {
            // 最小存活保护：面板刚显示不久时忽略关闭（防侧键重复事件）
            var aliveMs = _panelShownTick == 0
                ? double.MaxValue
                : (Stopwatch.GetTimestamp() - _panelShownTick) * 1000.0 / Stopwatch.Frequency;
            if (aliveMs < MinPanelAliveMs)
            {
                Dbg.Log($"TogglePanel -> IGNORED (panel alive {aliveMs:F0}ms < {MinPanelAliveMs}ms)");
                return;
            }
            Dbg.Log("TogglePanel -> HidePanel");
            _panel.HidePanel();
        }
        else
        {
            Dbg.Log("TogglePanel -> ShowPanel");
            _panel.ShowPanel(x, y);
        }
    }

    #endregion

    /// <summary>执行动作（记录统计）</summary>
    public void RunAction(ActionItem action)
    {
        _store.RecordActionRun(action.Id);
        using var _ = Suppress();
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = _executor.Execute(action);
            if (!result.Success)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show($"动作执行失败: {result.Error}\n\n{string.Join("\n", result.Log)}",
                        "速动", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        });
    }

    private bool IsInsidePanel(int x, int y)
    {
        var rect = _panel.GetPanelRect();
        if (rect is not { } r) return false;
        return x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h;
    }

    private static (int x, int y) CursorPosition()
    {
        return WpfCursor.Position;
    }

    private static double ElapsedMs(long startTick)
        => (Stopwatch.GetTimestamp() - startTick) * 1000.0 / Stopwatch.Frequency;

    public void Dispose()
    {
        _hooks.MouseDown -= OnMouseDown;
        _hooks.MouseUp -= OnMouseUp;
        _hooks.KeyDown -= OnKeyDown;
        _hooks.KeyUp -= OnKeyUp;
    }
}

/// <summary>
/// WPF 线程的鼠标位置帮助
/// </summary>
public static class WpfCursor
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public static (int x, int y) Position
    {
        get
        {
            GetCursorPos(out var p);
            return (p.X, p.Y);
        }
    }
}
