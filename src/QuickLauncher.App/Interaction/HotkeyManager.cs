using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace QuickLauncher.App.Interaction;

/// <summary>
/// 快捷键字符串解析（"Ctrl+Alt+P" ↔ modifiers + vk）
/// </summary>
public static class HotkeyParser
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public static readonly Dictionary<string, int> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["SPACE"] = 0x20, ["ENTER"] = 0x0D, ["ESC"] = 0x1B, ["TAB"] = 0x09,
        ["LEFT"] = 0x25, ["RIGHT"] = 0x27, ["UP"] = 0x26, ["DOWN"] = 0x28,
        ["HOME"] = 0x24, ["END"] = 0x23, ["INSERT"] = 0x2D, ["DELETE"] = 0x2E,
        ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD,
    };

    /// <summary>
    /// 解析 "Ctrl+Alt+P"。失败返回 false。
    /// </summary>
    public static bool TryParse(string? hotkey, out uint modifiers, out int vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts.Take(parts.Length - 1))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        var key = parts[^1];
        if (!KeyNames.TryGetValue(key, out vk))
        {
            if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
                vk = char.ToUpperInvariant(key[0]);
            else
                return false;
        }
        return true;
    }

    /// <summary>
    /// vk + modifiers → 标准显示字符串
    /// </summary>
    public static string Format(uint modifiers, int vk)
    {
        var sb = new StringBuilder();
        if ((modifiers & MOD_CONTROL) != 0) sb.Append("Ctrl+");
        if ((modifiers & MOD_ALT) != 0) sb.Append("Alt+");
        if ((modifiers & MOD_SHIFT) != 0) sb.Append("Shift+");
        if ((modifiers & MOD_WIN) != 0) sb.Append("Win+");
        var name = KeyNames.FirstOrDefault(k => k.Value == vk).Key
            ?? (vk >= 0x30 && vk <= 0x39 ? ((char)vk).ToString() : "?");
        sb.Append(name.Length == 1 ? name.ToUpperInvariant() : name);
        return sb.ToString();
    }
}

/// <summary>
/// 全局热键管理：通过 RegisterHotKey 注册，WM_HOTKEY 消息触发。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, Action> _handlers = new();
    private IntPtr _hwnd;
    private int _nextId = 1;
    private HwndSource? _source;

    /// <summary>注册快捷键。返回 false 表示冲突/失败。</summary>
    public bool Register(string hotkey, Action handler)
    {
        if (!HotkeyParser.TryParse(hotkey, out var mods, out var vk)) return false;
        if (_hwnd == IntPtr.Zero) return false;

        var id = _nextId++;
        // 加 MOD_NOREPEAT 避免按住时重复触发
        if (RegisterHotKey(_hwnd, id, mods | HotkeyParser.MOD_NOREPEAT, vk))
        {
            _handlers[id] = handler;
            return true;
        }
        return false;
    }

    /// <summary>附加到 WPF 窗口以接收 WM_HOTKEY</summary>
    public void Attach(System.Windows.Window window)
    {
        _source = PresentationSource.FromVisual(window) as HwndSource
            ?? new HwndSource(new HwndSourceParameters("QLHotkeySink") { ParentWindow = IntPtr.Zero });
        _hwnd = _source.Handle;
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void UnregisterAll()
    {
        if (_hwnd != IntPtr.Zero)
            foreach (var id in _handlers.Keys)
                UnregisterHotKey(_hwnd, id);
        _handlers.Clear();
    }

    public void Dispose() => UnregisterAll();
}
