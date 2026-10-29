using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using QuickLauncher.Engine;

namespace QuickLauncher.Steps;

/// <summary>
/// Win32 键鼠模拟与窗口帮助（P/Invoke）
/// </summary>
public static partial class Win32
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEINPUT
    {
        [FieldOffset(0)] public int dx;
        [FieldOffset(4)] public int dy;
        [FieldOffset(8)] public uint mouseData;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(20)] public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public static void SendKey(ushort vk, bool down = true)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vk;
        inputs[0].u.ki.wScan = 0;
        inputs[0].u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>按下并释放</summary>
    public static void Tap(ushort vk)
    {
        SendKey(vk, true);
        SendKey(vk, false);
    }

    /// <summary>组合键（如 Ctrl+C）：依次按下，反序释放</summary>
    public static void Combo(params ushort[] vks)
    {
        foreach (var v in vks) SendKey(v, true);
        foreach (var v in vks.Reverse()) SendKey(v, false);
    }

    public static void SendUnicodeChar(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0;
        inputs[0].u.ki.wScan = ch;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0;
        inputs[1].u.ki.wScan = ch;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>发送文本（Unicode 方式，支持中文）</summary>
    public static void SendText(string text)
    {
        foreach (var ch in text)
            SendUnicodeChar(ch);
    }

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ForegroundProcessName()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    public static bool Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
        return SetForegroundWindow(hWnd);
    }
}
