using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickLauncher.App.Hooks;

/// <summary>
/// 全局低层键鼠钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）。
/// 带自动重载机制：Windows 在钩子回调超时会卸载低层钩子，需检测并重装。
/// </summary>
public sealed class GlobalHooks : IDisposable
{
    // 低层钩子类型常量（避免依赖 WinForms 的 Internal 值）
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private HookProc? _kbProc;
    private HookProc? _msProc;
    private IntPtr _kbHook = IntPtr.Zero;
    private IntPtr _msHook = IntPtr.Zero;
    private readonly System.Timers.Timer _watchdog;
    private long _lastEventTick;
    private bool _suspended;

    public GlobalHooks()
    {
        _watchdog = new System.Timers.Timer(2000) { AutoReset = true };
        _watchdog.Elapsed += (_, _) => WatchdogCheck();
    }

    /// <summary>钩子被外部禁用（系统卸载）时触发，应重装</summary>
    public event Action? HooksLost;

    public bool IsSuspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (!_suspended) EnsureInstalled();
        }
    }

    #region 事件

    public event Action<KeyEventData>? KeyDown;
    public event Action<KeyEventData>? KeyUp;
    public event Action<MouseEventData>? MouseDown;
    public event Action<MouseEventData>? MouseUp;
    public event Action<MouseEventData>? MouseMove;
    public event Action<MouseEventData>? MouseWheel;

    public readonly struct KeyEventData
    {
        public int VkCode { get; init; }
        public bool Injected { get; init; }
        public bool IsKeyDown { get; init; }
    }

    public readonly struct MouseEventData
    {
        public int X { get; init; }
        public int Y { get; init; }
        public MouseButtons Button { get; init; }
        public int WheelDelta { get; init; }
        public bool Injected { get; init; }
        public MouseEventType Type { get; init; }
    }

    public enum MouseEventType { Down, Up, Move, Wheel }

    [Flags]
    public enum MouseButtons { None = 0, Left = 1, Right = 2, Middle = 4, X1 = 8, X2 = 16 }

    #endregion

    public void Install()
    {
        _kbProc = KbHookCallback;
        _msProc = MsHookCallback;
        var hMod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        var kbError = Marshal.GetLastWin32Error();
        _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, hMod, 0);
        var msError = Marshal.GetLastWin32Error();

        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
            throw new InvalidOperationException($"钩子安装失败: kb=0x{_kbHook:X} (err {kbError}), ms=0x{_msHook:X} (err {msError})");

        _watchdog.Start();
    }

    private void EnsureInstalled()
    {
        if (_suspended) return;
        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
        {
            // 重装
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc!, GetModuleHandle(null), 0);
            _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc!, GetModuleHandle(null), 0);
            _watchdog.Start();
        }
    }

    private void WatchdogCheck()
    {
        // 若长时间无事件且钩子句柄失效 -> 重装
        if (_suspended) return;
        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
        {
            EnsureInstalled();
            if (_kbHook != IntPtr.Zero && _msHook != IntPtr.Zero)
                HooksLost?.Invoke();
        }
    }

    private IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suspended)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isDown = wParam is 0x100 or 0x104; // WM_KEYDOWN / WM_SYSKEYDOWN
            var isUp = wParam is 0x101 or 0x105;   // WM_KEYUP / WM_SYSKEYUP
            if (isDown || isUp)
            {
                _lastEventTick = Stopwatch.GetTimestamp();
                var evt = new KeyEventData
                {
                    VkCode = (int)data.vkCode,
                    Injected = (data.flags & LLKHF_INJECTED) != 0,
                    IsKeyDown = isDown
                };
                if (isDown) KeyDown?.Invoke(evt);
                else KeyUp?.Invoke(evt);
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MsHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suspended)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var evt = new MouseEventData
            {
                X = data.pt.X,
                Y = data.pt.Y,
                Injected = (data.flags & LLMHF_INJECTED) != 0
            };

            switch (msg)
            {
                case 0x0201: // WM_LBUTTONDOWN
                    evt = evt with { Type = MouseEventType.Down, Button = MouseButtons.Left };
                    MouseDown?.Invoke(evt);
                    break;
                case 0x0202: // WM_LBUTTONUP
                    evt = evt with { Type = MouseEventType.Up, Button = MouseButtons.Left };
                    MouseUp?.Invoke(evt);
                    break;
                case 0x0204: // WM_RBUTTONDOWN
                    evt = evt with { Type = MouseEventType.Down, Button = MouseButtons.Right };
                    MouseDown?.Invoke(evt);
                    break;
                case 0x0205: // WM_RBUTTONUP
                    evt = evt with { Type = MouseEventType.Up, Button = MouseButtons.Right };
                    MouseUp?.Invoke(evt);
                    break;
                case 0x0207: // WM_MBUTTONDOWN
                    evt = evt with { Type = MouseEventType.Down, Button = MouseButtons.Middle };
                    MouseDown?.Invoke(evt);
                    break;
                case 0x0208: // WM_MBUTTONUP
                    evt = evt with { Type = MouseEventType.Up, Button = MouseButtons.Middle };
                    MouseUp?.Invoke(evt);
                    break;
                case 0x020B: // WM_XBUTTONDOWN
                    evt = evt with { Type = MouseEventType.Down, Button = (data.mouseData >> 16) == 1 ? MouseButtons.X1 : MouseButtons.X2 };
                    MouseDown?.Invoke(evt);
                    break;
                case 0x020C: // WM_XBUTTONUP
                    evt = evt with { Type = MouseEventType.Up, Button = (data.mouseData >> 16) == 1 ? MouseButtons.X1 : MouseButtons.X2 };
                    MouseUp?.Invoke(evt);
                    break;
                case 0x0200: // WM_MOUSEMOVE
                    evt = evt with { Type = MouseEventType.Move };
                    MouseMove?.Invoke(evt);
                    break;
                case 0x020A: // WM_MOUSEWHEEL
                    evt = evt with { Type = MouseEventType.Wheel, WheelDelta = (short)(data.mouseData >> 16) };
                    MouseWheel?.Invoke(evt);
                    break;
            }
            _lastEventTick = Stopwatch.GetTimestamp();
        }
        return CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _watchdog.Stop();
        _watchdog.Dispose();
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) UnhookWindowsHookEx(_msHook);
        _kbHook = IntPtr.Zero;
        _msHook = IntPtr.Zero;
    }
}
