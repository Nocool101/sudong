using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using QuickLauncher.App.Hooks;
using QuickLauncher.Models;
using QuickLauncher.Storage;

namespace QuickLauncher.App.Interaction;

/// <summary>
/// 扩展热键监听器（引导键体系）：按引导键进入 leader 模式，
/// 再按动作键（可加修饰键成三键）执行动作。单独按引导键可触发空动作键指令。
/// </summary>
public sealed class HotkeyLeaderWatcher : IDisposable
{
    private readonly GlobalHooks _hooks;
    private readonly ActionStore _store;
    private readonly Func<ActionItem, bool> _runAction;
    private readonly System.Timers.Timer _leaderTimeout;

    private List<HotkeyCommand> _commands = new();
    private bool _leaderActive;
    private long _leaderDownTick;
    private bool _leaderConsumed;

    /// <summary>leader 模式超时（ms）</summary>
    public int LeaderTimeoutMs { get; set; } = 2500;

    /// <summary>单独按引导键的判定：松开快于该值且未按其他键</summary>
    public int LeaderTapMs { get; set; } = 400;

    public HotkeyLeaderWatcher(
        GlobalHooks hooks,
        ActionStore store,
        Func<ActionItem, bool> runAction)
    {
        _hooks = hooks;
        _store = store;
        _runAction = runAction;

        _leaderTimeout = new System.Timers.Timer(LeaderTimeoutMs) { AutoReset = false };
        _leaderTimeout.Elapsed += (_, _) => _leaderActive = false;

        _hooks.KeyDown += OnKeyDown;
        _hooks.KeyUp += OnKeyUp;
        ReloadCommands();
    }

    public void ReloadCommands()
    {
        _commands = _store.LoadHotkeyCommands().Where(c => c.Enabled).ToList();
    }

    private static readonly Dictionary<string, int> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CapsLock"] = 0x14, ["Space"] = 0x20, ["Tab"] = 0x09, ["Enter"] = 0x0D,
        ["Esc"] = 0x1B, ["Backquote"] = 0xC0, ["`"] = 0xC0,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["Ctrl"] = 0x11, ["Alt"] = 0x12, ["Shift"] = 0x10, ["Win"] = 0x5B,
    };

    private static int? ParseKey(string? name)
        => name is not null && KeyNames.TryGetValue(name.Trim(), out var vk) ? vk : null;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// 校验修饰键组合是否与指令配置一致（如 Ctrl+Shift+CapsLock → P）。
    /// Modifiers 为空表示不要求修饰键；配置里的每个修饰键都必须按住，未配置的必须松开。
    /// </summary>
    private static bool ModifiersMatch(string? modifiers)
    {
        var expected = (modifiers ?? "").ToLowerInvariant();
        bool Need(string name) => expected.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(name);
        return IsDown(0x11) == Need("ctrl")
            && IsDown(0x12) == Need("alt")
            && IsDown(0x10) == Need("shift")
            && (IsDown(0x5B) || IsDown(0x5C)) == Need("win");
    }

    private void OnKeyDown(GlobalHooks.KeyEventData e)
    {
        // 模态对话框期间静默，避免误触发了扩展热键
        if (App.IsModalDialogOpen) return;

        // leader 模式已激活：尝试匹配动作键
        if (_leaderActive)
        {
            _leaderActive = false;
            _leaderTimeout.Stop();
            _leaderConsumed = true; // 引导键抬起时不再触发 tap

            var match = _commands.FirstOrDefault(c =>
                ParseKey(c.LeaderKey) == _leaderVk &&
                ParseKey(c.ActionKey) == e.VkCode &&
                ModifiersMatch(c.Modifiers));
            if (match is not null)
            {
                var action = _store.LoadAction(match.ActionId);
                if (action is not null) _runAction(action);
            }
            return;
        }

        // 引导键按下
        var leaderVk = _commands
            .Select(c => ParseKey(c.LeaderKey))
            .FirstOrDefault(vk => vk == e.VkCode);
        if (leaderVk is not null)
        {
            _leaderVk = leaderVk.Value;
            _leaderActive = true;
            _leaderConsumed = false;
            _leaderDownTick = Stopwatch.GetTimestamp();
            _leaderTimeout.Stop();
            _leaderTimeout.Interval = LeaderTimeoutMs;
            _leaderTimeout.Start();
        }
    }

    private int _leaderVk;

    private void OnKeyUp(GlobalHooks.KeyEventData e)
    {
        // 模态对话框期间静默
        if (App.IsModalDialogOpen) return;

        // 单独按引导键（快速抬起且未按其他键）→ 执行空动作键指令
        if (e.VkCode == _leaderVk && !_leaderConsumed)
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - _leaderDownTick) * 1000.0 / Stopwatch.Frequency;
            _leaderActive = false;
            _leaderTimeout.Stop();
            _leaderConsumed = true;

            if (elapsedMs <= LeaderTapMs)
            {
                var match = _commands.FirstOrDefault(c =>
                    ParseKey(c.LeaderKey) == e.VkCode && string.IsNullOrEmpty(c.ActionKey));
                if (match is not null)
                {
                    var action = _store.LoadAction(match.ActionId);
                    if (action is not null) _runAction(action);
                }
            }
        }
    }

    public void Dispose()
    {
        _hooks.KeyDown -= OnKeyDown;
        _hooks.KeyUp -= OnKeyUp;
        _leaderTimeout.Dispose();
    }
}
