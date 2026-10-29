using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickLauncher.App.Views;

/// <summary>
/// 按键捕获对话框：录制期间挂载临时低级键盘钩子（WH_KEYBOARD_LL），
/// 不依赖窗口焦点，任何按键组合都能可靠捕获。
/// 输出 Quicker 风格按键语法（^=Ctrl !=Alt +=Shift #=Win）。
/// </summary>
public partial class KeyCaptureDialog : Window
{
    private const int WH_KEYBOARD_LL = 13;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private LowLevelKeyboardProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public string CapturedKey { get; private set; } = "";

    public KeyCaptureDialog()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null!), 0);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var isDown = wParam is 0x100 or 0x104; // WM_KEYDOWN / WM_SYSKEYDOWN
            if (isDown)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vk = (int)data.vkCode;
                Dispatcher.BeginInvoke(new Action(() => HandleVk(vk)));
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>处理一个按下的虚拟键码</summary>
    private void HandleVk(int vk)
    {
        // Esc 单独按 = 取消
        if (vk == 0x1B)
        {
            DialogResult = false;
            Close();
            return;
        }

        // Enter 单独按 = 确认当前已录制的按键
        if (vk == 0x0D && !string.IsNullOrEmpty(CapturedKey))
        {
            DialogResult = true;
            Close();
            return;
        }

        // 只按修饰键不算组合
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0x5B or 0x5C)
            return;

        var sb = new StringBuilder();
        if ((GetKeyState(0x11) & 0x8000) != 0) sb.Append('^');           // Ctrl
        if ((GetKeyState(0x12) & 0x8000) != 0) sb.Append('!');           // Alt
        if ((GetKeyState(0x10) & 0x8000) != 0) sb.Append('+');           // Shift
        if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) sb.Append('#'); // Win

        var token = VkToken(vk);
        if (token is null) return;

        sb.Append(token);
        CapturedKey = sb.ToString();
        KeyText.Text = CapturedKey;
        DialogResult = true;
        Close();
    }

    private static string? VkToken(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => ((char)('0' + vk - 0x60)).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        0x0D => "ENTER",
        0x09 => "TAB",
        0x20 => "SPACE",
        0x08 => "BACKSPACE",
        0x2E => "DELETE",
        0x2D => "INSERT",
        0x24 => "HOME",
        0x23 => "END",
        0x21 => "PGUP",
        0x22 => "PGDN",
        0x26 => "UP",
        0x28 => "DOWN",
        0x25 => "LEFT",
        0x27 => "RIGHT",
        0xBC => ",", 0xBE => ".", 0xBF => "/",
        0xBA => ";", 0xDE => "'", 0xDB => "[",
        0xDD => "]", 0xDC => "\\", 0xBD => "-",
        0xBB => "=", 0xC0 => "`",
        _ => null
    };
}
