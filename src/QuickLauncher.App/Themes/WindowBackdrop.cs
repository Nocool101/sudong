using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickLauncher.App.Themes;

/// <summary>
/// 窗口毛玻璃背板：调用 DWM 开启系统级 Acrylic / Mica 背景。
///
/// 【为什么需要真背板】
/// 纯 WPF 的半透明只是「把后面的东西透出来」，桌面壁纸是清晰的、没有模糊，
/// 玻璃感会退化成「半透明色块」。真正的毛玻璃必须由 DWM 对窗口背后的桌面做
/// 高斯模糊，再把模糊结果合成进来。这只能走系统 API：
///   • Windows 11 22H2+（build 22621+）：DWMWA_SYSTEMBACKDROP_TYPE = Mica / Acrylic，
///     顺带获得系统圆角与阴影，是 Win11 原生观感。
///   • Windows 10 1803+：SetWindowCompositionAttribute + ACCENT_ENABLE_ACRYLICBLURBEHIND，
///     自带磨砂质感（颜色可自定义）。
///
/// 【降级策略】
/// 所有调用都包在 try/catch 里，任何一步失败都只是「没有模糊」，
/// 而窗口自身的半透明底色依然成立——绝不会出现白屏或透明到看不见的情况。
/// </summary>
public static class WindowBackdrop
{
    private enum AccentState
    {
        Disabled = 0,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW versionInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_OSVERSIONINFOW
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string dwCSDVersion;
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MICA = 2;
    private const int DWMSBT_ACRYLIC = 3;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int GWL_EXSTYLE = -20;

    /// <summary>由磨砂底色亮度推断当前主题是否为暗色（用于同步系统标题栏配色）</summary>
    private static bool IsDarkTitleBar(Color tint)
        => (tint.R + tint.G + tint.B) / 3 < 128;

    /// <summary>系统是否为 Windows 11 22H2 及以上（支持 DWMWA_SYSTEMBACKDROP_TYPE）</summary>
    public static bool SupportsSystemBackdrop => BuildNumber >= 22621;

    private static int BuildNumber
    {
        get
        {
            try
            {
                var info = new RTL_OSVERSIONINFOW
                {
                    dwOSVersionInfoSize = System.Runtime.InteropServices.Marshal.SizeOf<RTL_OSVERSIONINFOW>()
                };
                if (RtlGetVersion(ref info) == 0) return info.dwBuildNumber;
            }
            catch
            {
                // 取不到就按旧版处理
            }
            return 0;
        }
    }

    /// <summary>
    /// 为窗口开启毛玻璃背板。返回是否成功开启（失败仅表示「没有模糊」，界面依然可正常显示）。
    /// </summary>
    public static bool TryApply(Window window, Color tint)
    {
        if (window is null) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        // 分层窗口（AllowsTransparency=True）用不了 DWM 系统背板，但 Win10 风格模糊仍有效
        var layered = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0;

        try
        {
            // 系统标题栏跟随应用主题：深色时强制暗标题栏，避免「深色内容 + 浅色标题栏」的割裂
            var dark = IsDarkTitleBar(tint);
            var immersive = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref immersive, sizeof(int));

            // Win11 22H2+：系统级 Acrylic（同时获得系统圆角与阴影）
            if (!layered && SupportsSystemBackdrop)
            {
                var backdrop = DWMSBT_ACRYLIC;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0)
                {
                    var corner = DWMWCP_ROUND;
                    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                    return true;
                }
            }

            // Win10 1803+ / 分层窗口：SetWindowCompositionAttribute 自定义磨砂
            var data = new WindowCompositionAttributeData
            {
                Attribute = (int)AccentState.EnableAcrylicBlurBehind,
                Data = Marshal.AllocHGlobal(sizeof(int)),
                SizeOfData = sizeof(int)
            };
            try
            {
                // 颜色按 ABGR 排列
                var packed = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
                Marshal.WriteInt32(data.Data, packed);
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(data.Data);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>对当前所有已打开的窗口重新应用背板（主题切换后底色变了要跟着换）</summary>
    public static void ReapplyAll(Color tint)
    {
        var windows = Application.Current?.Windows;
        if (windows is null) return;
        foreach (Window window in windows)
        {
            try { TryApply(window, tint); }
            catch { /* 单个窗口失败不影响其它 */ }
        }
    }
}
