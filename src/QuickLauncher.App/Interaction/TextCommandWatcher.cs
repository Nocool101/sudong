using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Timers;
using QuickLauncher.App.Hooks;
using QuickLauncher.App.Scenes;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Storage;

namespace QuickLauncher.App.Interaction;

/// <summary>
/// 文本指令监听器：监听键盘输入，匹配"缩写词 + 触发字符"执行动作。
/// 仿 Quicker 文本指令：
/// - 用户键入缩写词后按 Tab（或自定义触发字符）执行
/// - 支持退格修正、缓冲超时清空、进程绑定
/// </summary>
public sealed class TextCommandWatcher : IDisposable
{
    private readonly GlobalHooks _hooks;
    private readonly ActionStore _store;
    private readonly StepExecutor _executor;
    private SceneMatcher _matcher;
    private readonly AppSettings _settings;
    private readonly Func<ActionItem, bool> _runAction;
    private readonly System.Timers.Timer _clearTimer;

    private readonly StringBuilder _buffer = new();
    private List<TextCommand> _commands = new();
    private DateTime _loadedAt = DateTime.MinValue;

    /// <summary>缓冲清空超时（ms）</summary>
    public int BufferTimeoutMs { get; set; } = 3000;

    public TextCommandWatcher(
        GlobalHooks hooks,
        ActionStore store,
        StepExecutor executor,
        SceneMatcher matcher,
        AppSettings settings,
        Func<ActionItem, bool> runAction)
    {
        _hooks = hooks;
        _store = store;
        _executor = executor;
        _matcher = matcher;
        _settings = settings;
        _runAction = runAction;

        _clearTimer = new System.Timers.Timer(BufferTimeoutMs) { AutoReset = false };
        _clearTimer.Elapsed += (_, _) => _buffer.Clear();

        _hooks.KeyDown += OnKeyDown;
        ReloadCommands();
    }

    /// <summary>替换匹配器（App 层场景数据变更后调用）</summary>
    public void ReplaceMatcher(SceneMatcher matcher) => _matcher = matcher;

    /// <summary>重新加载指令（编辑器保存后调用）</summary>
    public void ReloadCommands()
    {
        _commands = _store.LoadTextCommands().Where(c => c.Enabled).ToList();
        _loadedAt = DateTime.UtcNow;
    }

    private void OnKeyDown(GlobalHooks.KeyEventData e)
    {
        // 模态对话框期间静默，避免误触发了文本指令
        if (App.IsModalDialogOpen) return;

        // Tab 触发
        if (e.VkCode == 0x09)
        {
            TryExecute();
            return;
        }

        // 退格：移除最后一个字符
        if (e.VkCode == 0x08)
        {
            if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1);
            ResetTimer();
            return;
        }

        // Esc：清空
        if (e.VkCode == 0x1B)
        {
            _buffer.Clear();
            return;
        }

        // 可打印字符追加
        var ch = VkToChar(e.VkCode, e.IsKeyDown);
        if (ch is not null)
        {
            _buffer.Append(ch.Value);
            if (_buffer.Length > 32) _buffer.Remove(0, _buffer.Length - 32);
            ResetTimer();
        }
    }

    private void ResetTimer()
    {
        _clearTimer.Stop();
        _clearTimer.Interval = BufferTimeoutMs;
        _clearTimer.Start();
    }

    private void TryExecute()
    {
        var text = _buffer.ToString();
        _buffer.Clear();

        if (string.IsNullOrEmpty(text)) return;

        var (process, _, _) = SceneMatcher.GetForegroundInfo();
        var commands = _commands
            .Where(c => c.Processes.Count == 0 || c.Processes.Any(p => SceneMatcher.Wildcard(process, p)))
            .ToList();

        // 1. 精确匹配缩写词
        var exact = commands.FirstOrDefault(c =>
            string.Equals(c.Abbreviation, text, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            Execute(exact);
            return;
        }

        // 2. 正则匹配
        foreach (var cmd in commands.Where(c => c.IsRegex))
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(text, cmd.Abbreviation);
                if (m.Success)
                {
                    var param = m.Groups.Count > 1 ? m.Groups[1].Value : text;
                    Execute(cmd, param);
                    return;
                }
            }
            catch { /* 无效正则忽略 */ }
        }
    }

    private void Execute(TextCommand command, string? param = null)
    {
        var action = _store.LoadAction(command.ActionId);
        if (action is null) return;

        // 输入法上屏处理：模拟退格消除已键入的缩写词
        var abbrLen = command.Abbreviation.Length;
        if (param is null && abbrLen > 0)
        {
            for (int i = 0; i < abbrLen; i++)
                Steps.Win32.SendKey(0x08); // Backspace
            System.Threading.Thread.Sleep(30);
        }

        if (param is not null)
        {
            // 将参数传入动作（通过临时输入参数执行）
            System.Threading.Tasks.Task.Run(() =>
            {
                var inputs = new Dictionary<string, string> { ["param"] = param, ["quicker_in_param"] = param };
                _executor.Execute(action, inputs);
            });
        }
        else
        {
            _runAction(action);
        }
    }

    private static char? VkToChar(int vk, bool isDown)
    {
        if (!isDown) return null;
        // A-Z
        if (vk is >= 0x41 and <= 0x5A)
            return (char)vk;
        // 0-9
        if (vk is >= 0x30 and <= 0x39)
            return (char)vk;
        // 小键盘 0-9
        if (vk is >= 0x60 and <= 0x69)
            return (char)(vk - 0x60 + '0');
        // 常用符号
        return vk switch
        {
            0xBD => '-', 0xBB => '=', 0xDB => '[', 0xDD => ']',
            0xBA => ';', 0xDE => '\'', 0xBC => ',', 0xBE => '.', 0xBF => '/',
            0xC0 => '`', 0xDC => '\\',
            0x6F => '/', 0x6A => '*', 0x6D => '-', 0x6B => '+',
            _ => (char?)null
        };
    }

    public void Dispose()
    {
        _hooks.KeyDown -= OnKeyDown;
        _clearTimer.Dispose();
    }
}
