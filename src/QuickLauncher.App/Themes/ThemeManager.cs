using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickLauncher.App.Themes;

/// <summary>应用主题模式</summary>
public enum AppTheme
{
    /// <summary>跟随系统</summary>
    System,
    /// <summary>始终暗色</summary>
    Dark,
    /// <summary>始终亮色</summary>
    Light,
}

/// <summary>
/// 主题引擎：在「暗色 / 亮色」两套配色之间整体切换。
///
/// 【为什么用 Color 资源而不是直接换画笔】
/// 1) WPF 会「冻结」被多处共享引用的 Freezable（画笔），冻结后属性只读，
///    运行时改色会抛「指定属性为只读」。App.xaml 里给画笔的 Color 挂上
///    DynamicResource 之后，画笔自身不可冻结，才能安全改色。
/// 2) StaticResource 在解析时就把值固化了，换字典不会让已解析的控件重新取色；
///    DynamicResource 会跟随资源变化，但只对「颜色资源」这一层用，
///    控件模板本身仍用 StaticResource 引用 Brush.*，避免大量动态解析开销。
///
/// 【系统主题跟随】
/// 读取 HKCU\...\Themes\Personalize\AppsUseLightTheme（1 = 亮色）判断系统当前处于哪种主题；
/// 再用一条 message-only 窗口监听 WM_SETTINGCHANGE，用户在系统里切换主题时应用会实时跟随。
/// </summary>
public static class ThemeManager
{
    /// <summary>主题切换完成（参数：是否为暗色）</summary>
    public static event Action<bool>? ThemeChanged;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int WM_SETTINGCHANGE = 0x001A;

    /// <summary>
    /// 实色令牌：(颜色资源键, 画笔键, 暗色值, 亮色值)。
    /// alpha 一并在此定义：玻璃质感靠半透明实现。
    /// </summary>
    private static readonly (string ColorKey, string BrushKey, Color Dark, Color Light)[] Tokens =
    {
        // ── 表面层（半透明，透出背后的毛玻璃）────────────────────
        ("C.BgBase", "Brush.BgBase", Color.FromArgb(0xEB, 0x0C, 0x10, 0x18), Color.FromArgb(0xF2, 0xF8, 0xFA, 0xFD)),
        ("C.BgSide", "Brush.BgSide", Color.FromArgb(0xCB, 0x09, 0x0D, 0x14), Color.FromArgb(0xD8, 0xEE, 0xF2, 0xF7)),
        ("C.BgCard", "Brush.BgCard", Color.FromArgb(0xC4, 0x14, 0x1A, 0x26), Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
        ("C.BgCardHover", "Brush.BgCardHover", Color.FromArgb(0xEE, 0x1E, 0x26, 0x38), Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
        ("C.Scrim", "Brush.Scrim", Color.FromArgb(0xF6, 0x10, 0x14, 0x1E), Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF)),
        ("C.PanelBg", "Brush.PanelBg", Color.FromArgb(0xED, 0x0C, 0x10, 0x19), Color.FromArgb(0xF4, 0xF8, 0xFA, 0xFD)),
        ("C.Tile", "Brush.Tile", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)),
        ("C.TileEmpty", "Brush.TileEmpty", Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x50, 0xE2, 0xE8, 0xF0)),

        // ── 描边与高光 ──────────────────────────────────────────
        ("C.Stroke", "Brush.Stroke", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x1C, 0x0F, 0x17, 0x2A)),
        ("C.StrokeStrong", "Brush.StrokeStrong", Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x34, 0x0F, 0x17, 0x2A)),
        ("C.GlassHighlight", "Brush.GlassHighlight", Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),

        // ── 文字 ────────────────────────────────────────────────
        ("C.TextPrimary", "Brush.TextPrimary", Color.FromArgb(0xFF, 0xF8, 0xFA, 0xFC), Color.FromArgb(0xFF, 0x0F, 0x17, 0x2A)),
        ("C.TextSecondary", "Brush.TextSecondary", Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8), Color.FromArgb(0xFF, 0x33, 0x41, 0x55)),
        ("C.TextMuted", "Brush.TextMuted", Color.FromArgb(0xFF, 0x64, 0x74, 0x8B), Color.FromArgb(0xFF, 0x64, 0x74, 0x8B)),

        // ── 品牌与状态色 ────────────────────────────────────────
        ("C.Brand", "Brush.Brand", Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8), Color.FromArgb(0xFF, 0x02, 0x84, 0xC7)),
        ("C.Brand2", "Brush.Brand2", Color.FromArgb(0xFF, 0x81, 0x8C, 0xF8), Color.FromArgb(0xFF, 0x63, 0x66, 0xF1)),
        ("C.Accent", "Brush.Accent", Color.FromArgb(0xFF, 0x34, 0xD3, 0x99), Color.FromArgb(0xFF, 0x05, 0x96, 0x69)),
        ("C.Danger", "Brush.Danger", Color.FromArgb(0xFF, 0xFB, 0x71, 0x85), Color.FromArgb(0xFF, 0xE1, 0x1D, 0x48)),
        ("C.Warn", "Brush.Warn", Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24), Color.FromArgb(0xFF, 0xD9, 0x77, 0x06)),

        // ── 交互叠加层 ──────────────────────────────────────────
        ("C.HoverOverlay", "Brush.HoverOverlay", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x12, 0x0B, 0x15, 0x26)),
        ("C.PressOverlay", "Brush.PressOverlay", Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x22, 0x0B, 0x15, 0x26)),
    };

    /// <summary>渐变令牌：(画笔键, 停止点下标, 颜色资源键, 暗色值, 亮色值)</summary>
    private static readonly (string BrushKey, int Stop, string ColorKey, Color Dark, Color Light)[] GradientTokens =
    {
        ("Brush.Gradient", 0, "C.Grad1", Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8), Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)),
        ("Brush.Gradient", 1, "C.Grad2", Color.FromArgb(0xFF, 0x63, 0x66, 0xF1), Color.FromArgb(0xFF, 0x4F, 0x46, 0xE5)),
        ("Brush.Gradient", 2, "C.Grad3", Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7), Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)),

        ("Brush.GradientSoft", 0, "C.GradSoft1", Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8), Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)),
        ("Brush.GradientSoft", 1, "C.GradSoft2", Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7), Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)),

        ("Brush.Specular", 0, "C.SpecularTop", Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
        ("Brush.Specular", 1, "C.SpecularBottom", Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),

        ("Brush.Bloom1", 0, "C.Bloom1", Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8), Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)),
        ("Brush.Bloom1", 1, "C.Bloom1Mid", Color.FromArgb(0x80, 0x38, 0xBD, 0xF8), Color.FromArgb(0x80, 0x0E, 0xA5, 0xE9)),
        ("Brush.Bloom1", 2, "C.Bloom1End", Color.FromArgb(0x00, 0x38, 0xBD, 0xF8), Color.FromArgb(0x00, 0x0E, 0xA5, 0xE9)),

        ("Brush.Bloom2", 0, "C.Bloom2", Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7), Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)),
        ("Brush.Bloom2", 1, "C.Bloom2Mid", Color.FromArgb(0x80, 0xA8, 0x55, 0xF7), Color.FromArgb(0x80, 0x93, 0x33, 0xEA)),
        ("Brush.Bloom2", 2, "C.Bloom2End", Color.FromArgb(0x00, 0xA8, 0x55, 0xF7), Color.FromArgb(0x00, 0x93, 0x33, 0xEA)),

        ("Brush.Bloom3", 0, "C.Bloom3", Color.FromArgb(0xFF, 0x22, 0xD3, 0xEE), Color.FromArgb(0xFF, 0x06, 0xB6, 0xD4)),
        ("Brush.Bloom3", 1, "C.Bloom3Mid", Color.FromArgb(0x80, 0x22, 0xD3, 0xEE), Color.FromArgb(0x80, 0x06, 0xB6, 0xD4)),
        ("Brush.Bloom3", 2, "C.Bloom3End", Color.FromArgb(0x00, 0x22, 0xD3, 0xEE), Color.FromArgb(0x00, 0x06, 0xB6, 0xD4)),
    };

    /// <summary>当前生效的显示主题（System 解析后的实际结果）</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>用户选择的模式</summary>
    public static AppTheme Mode { get; private set; } = AppTheme.System;

    private static bool _initialized;
    private static HwndSource? _watcher;

    /// <summary>按设置字符串解析模式</summary>
    public static AppTheme ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => AppTheme.System,
    };

    /// <summary>模式 → 设置字符串</summary>
    public static string ToSettingValue(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "Dark",
        AppTheme.Light => "Light",
        _ => "System",
    };

    /// <summary>初始化主题：应用配色、启动系统主题监听</summary>
    public static void Initialize(AppTheme mode)
    {
        Mode = mode;
        Apply(mode);

        if (_initialized) return;
        _initialized = true;
        StartSystemWatcher();
        RegisterBackdropForAllWindows();
    }

    /// <summary>切换主题并通知界面</summary>
    public static void SetTheme(AppTheme mode)
    {
        Mode = mode;
        Apply(mode);
    }

    private static void Apply(AppTheme mode)
    {
        var dark = mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };

        IsDark = dark;
        ApplyTokens(dark);
        // 已打开的窗口需要重新应用背板（磨砂颜色随主题变化）
        try { WindowBackdrop.ReapplyAll(Tint(dark)); } catch { /* 无窗口或失败时忽略 */ }
        ThemeChanged?.Invoke(dark);
    }

    /// <summary>背板磨砂颜色（跟随主题）</summary>
    private static Color Tint(bool dark)
        => dark ? Color.FromArgb(0x80, 0x0C, 0x10, 0x18) : Color.FromArgb(0x80, 0xF8, 0xFA, 0xFD);

    private static void ApplyTokens(bool dark)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        // 实色令牌：换掉画笔实例，而不是改写它。
        //
        // 【为什么必须换实例】WPF 会冻结被多处共享引用的 Freezable（画笔），
        // 冻结后其 Color 等属性只读，运行时改写会抛「指定属性为只读」。
        // 所有消费端都用 DynamicResource 引用画笔，因此替换字典里的实例即可整体换色。
        foreach (var t in Tokens)
        {
            var color = dark ? t.Dark : t.Light;
            resources[t.ColorKey] = color;
            resources[t.BrushKey] = new SolidColorBrush(color);
        }

        // ── 品牌渐变（青蓝 → 蓝 → 紫，135°）──
        resources["Brush.Gradient"] = Linear(new[]
        {
            (0.0, dark ? Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8) : Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)),
            (0.5, dark ? Color.FromArgb(0xFF, 0x63, 0x66, 0xF1) : Color.FromArgb(0xFF, 0x4F, 0x46, 0xE5)),
            (1.0, dark ? Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7) : Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)),
        });

        resources["Brush.GradientSoft"] = Linear(new[]
        {
            (0.0, dark ? Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8) : Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)),
            (1.0, dark ? Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7) : Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)),
        }, opacity: dark ? 0.20 : 0.14);

        // ── 玻璃上缘高光 ──
        resources["Brush.Specular"] = Linear(new[]
        {
            (0.0, dark ? Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            (1.0, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
        });

        // ── 分隔线：两端渐隐 ──
        resources["Brush.Divider"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(dark ? Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)
                                      : Color.FromArgb(0x33, 0x10, 0x1A, 0x2E), 0.5),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            }
        };

        // ── 极光色斑 ──
        resources["Brush.Bloom1"] = Radial(Bloom(dark,
            Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8), Color.FromArgb(0xFF, 0x0E, 0xA5, 0xE9)));
        resources["Brush.Bloom2"] = Radial(Bloom(dark,
            Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6), Color.FromArgb(0xFF, 0x7C, 0x3A, 0xED)));
        resources["Brush.Bloom3"] = Radial(Bloom(dark,
            Color.FromArgb(0xFF, 0x22, 0xD3, 0xEE), Color.FromArgb(0xFF, 0x06, 0xB6, 0xD4)));
    }

    /// <summary>极光色斑的三段色（实心 → 半透 → 全透）</summary>
    private static (double Offset, Color Color)[] Bloom(bool dark, Color d, Color l)
    {
        var c = dark ? d : l;
        return new[]
        {
            (0.0, c),
            (0.5, Color.FromArgb(0x80, c.R, c.G, c.B)),
            (1.0, Color.FromArgb(0x00, c.R, c.G, c.B)),
        };
    }

    private static LinearGradientBrush Linear((double Offset, Color Color)[] stops, double opacity = 1.0)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            Opacity = opacity,
        };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    private static RadialGradientBrush Radial((double Offset, Color Color)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    /// <summary>按资源键取画笔（返回的是同一实例，主题切换后自动跟随）</summary>
    public static Brush Get(string key)
        => Application.Current?.Resources[key] as Brush ?? Brushes.White;

    /// <summary>
    /// 为所有窗口（含各级弹窗）统一开启系统毛玻璃背板。
    /// 用类级事件处理器一次性注册，避免在每个窗口里重复写样板代码。
    /// </summary>
    private static void RegisterBackdropForAllWindows()
    {
        try
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is not Window w) return;
                    try { WindowBackdrop.TryApply(w, Tint(IsDark)); }
                    catch { /* 单个窗口失败不影响其它 */ }
                }));
        }
        catch
        {
            // 注册失败不影响手动主题切换
        }
    }

    /// <summary>读取系统当前主题是否为暗色</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch
        {
            // 读取失败（组策略锁定等）→ 回退暗色
        }
        return true;
    }

    /// <summary>监听系统主题变化：系统里切换浅/深色时应用实时跟随</summary>
    private static void StartSystemWatcher()
    {
        try
        {
            var parameters = new HwndSourceParameters("SudongThemeWatcher")
            {
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE：仅接收消息的隐藏窗口
                WindowStyle = 0,
            };
            _watcher = new HwndSource(parameters);
            _watcher.AddHook(WndProc);
        }
        catch
        {
            // 监听失败不影响手动切换主题
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && Mode == AppTheme.System)
        {
            try
            {
                var key = Marshal.PtrToStringAnsi(lParam);
                if (key is null || key.Contains("ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
                    Apply(AppTheme.System);
            }
            catch
            {
                // 忽略：下一次 WM_SETTINGCHANGE 会再触发
            }
        }
        handled = false;
        return IntPtr.Zero;
    }
}
