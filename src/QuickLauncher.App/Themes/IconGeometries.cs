using System.Collections.Generic;
using System.Windows.Media;

namespace QuickLauncher.App.Themes;

/// <summary>
/// 内置矢量图标库。
///
/// 全部图标绘制在 24×24 网格上，采用「描边」风格（与 Lucide / Fluent 同源），
/// 由 <see cref="QuickLauncher.App.Views.IconView"/> 统一渲染：
/// Stroke = Foreground，StrokeThickness = 1.75 × 24 / Size，圆头圆角连接。
///
/// 为什么不用字符（⚡▤⌨）或 Emoji：字符图标依赖系统字体回退，不同 Windows 版本
/// 字形差异大且带彩色 Emoji 噪点；矢量图标在任何机器上都是同一套形状与粗细。
/// </summary>
public static class IconGeometries
{
    /// <summary>图标名 → SVG 风格路径数据（24×24 坐标系）</summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        // ── 导航 ──────────────────────────────────────────────
        ["Bolt"] = "M15 4.8 L5.4 15 L10.6 15 L9 19.2 L18.6 9 L13.2 9 Z",
        ["Layers"] = "M12 3 L2 8 L12 13 L22 8 Z M2 12 L12 17 L22 12 M2 16 L12 21 L22 16",
        ["Terminal"] = "M5 7 L10 12 L5 17 M12.5 17 H19",
        ["Sparkles"] = "M12 3 L13.9 8.1 L19 10 L13.9 11.9 L12 17 L10.1 11.9 L5 10 L10.1 8.1 Z " +
                       "M18.5 14.5 L19.3 16.3 L21.1 17.1 L19.3 17.9 L18.5 19.7 L17.7 17.9 L15.9 17.1 L17.7 16.3 Z",
        ["Sliders"] = "M3 6 H7.5 M10.5 6 H21 M3 12 H15 M18 12 H21 M3 18 H5.5 M8.5 18 H21 " +
                      "M9 4.5 A1.5 1.5 0 1 0 9 7.5 A1.5 1.5 0 1 0 9 4.5 " +
                      "M16.5 10.5 A1.5 1.5 0 1 0 16.5 13.5 A1.5 1.5 0 1 0 16.5 10.5 " +
                      "M7 16.5 A1.5 1.5 0 1 0 7 19.5 A1.5 1.5 0 1 0 7 16.5",

        // ── 通用动作 ──────────────────────────────────────────
        ["Plus"] = "M12 5 V19 M5 12 H19",
        ["Play"] = "M8 5 L19 12 L8 19 Z",
        ["Pencil"] = "M4 20 L5 16 L16 5 L19 8 L8 19 Z M14 6 L17 9",
        ["Trash"] = "M4 7 H20 M10 7 V5 H14 V7 M6 7 L7 20 H17 L18 7 M10 11 V16 M14 11 V16",
        ["Download"] = "M12 4 V15 M8 11 L12 15 L16 11 M5 19 H19",
        ["Upload"] = "M12 16 V5 M8 9 L12 5 L16 9 M5 19 H19",
        ["Search"] = "M18 11 A7 7 0 1 0 4 11 A7 7 0 1 0 18 11 M16 16 L21 21",
        ["Close"] = "M6 6 L18 18 M18 6 L6 18",
        ["Check"] = "M5 13 L9 17 L19 7",
        ["Refresh"] = "M20 12 A8 8 0 1 1 17.5 6.2 M20 4 V8 H16",
        ["ArrowUp"] = "M12 20 V5 M6 11 L12 5 L18 11",
        ["ArrowDown"] = "M12 4 V19 M6 13 L12 19 L18 13",
        ["ChevronLeft"] = "M15 5 L8 12 L15 19",
        ["ChevronRight"] = "M9 5 L16 12 L9 19",
        ["Save"] = "M5 3 H17 L19 5 V21 H5 Z M8 3 V9 H15 V3 M8 21 V14 H16 V21",
        ["Copy"] = "M9 8 H20 V21 H9 Z M5 16 H4 V3 H17 V4",
        ["Record"] = "M12 7 A5 5 0 1 0 12 17 A5 5 0 1 0 12 7",
        ["Info"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 11 V17 M12 7.6 V7.7",
        ["Warning"] = "M12 4 L21.5 20 H2.5 Z M12 10 V14.5 M12 17.4 V17.5",

        // ── 动作类型 ──────────────────────────────────────────
        ["Monitor"] = "M3 5 H21 V16 H3 Z M9 20 H15 M12 16 V20",
        ["File"] = "M6 3 H14 L19 8 V21 H6 Z M14 3 V8 H19 M9 13 H16 M9 17 H16",
        ["Folder"] = "M3 7 H9 L11 9 H21 V20 H3 Z",
        ["Globe"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M3.6 9 H20.4 M3.6 15 H20.4 " +
                    "M12 3 C9 6.5 9 17.5 12 21 C15 17.5 15 6.5 12 3",
        ["Keyboard"] = "M2.5 6 H21.5 V18 H2.5 Z M6 9.5 V11 M9.5 9.5 V11 M13 9.5 V11 M16.5 9.5 V11 M8 14.5 H16",
        ["Message"] = "M4 5 H20 V16 H11 L7 20 V16 H4 Z",
        ["Scroll"] = "M7 3 H17 V21 H7 Z M10 8 H14 M10 12 H14 M10 16 H12",
        ["Page"] = "M5 3 H19 V21 H5 Z M9 8 H15 M9 12 H15 M9 16 H13",
        ["Grid"] = "M3 6 H10 V13 H3 Z M14 6 H21 V13 H14 Z M3 16 H10 V21 H3 Z M14 16 H21 V21 H14 Z",
        ["Rocket"] = "M12 2.5 C15 4.5 16.8 7.8 16.8 11 L13.5 14.5 H10.5 L7.2 11 C7.2 7.8 9 4.5 12 2.5 Z " +
                     "M9.5 11 L6.5 17.5 L10.5 15.5 M14.5 11 L17.5 17.5 L13.5 15.5 M12 8 V11",
        ["Import"] = "M12 3 V14 M8 10 L12 14 L16 10 M4 20 H20",
        ["Export"] = "M12 21 V10 M8 14 L12 10 L16 14 M4 4 H20",

        // ── 主题 / 外观 ───────────────────────────────────────
        ["Sun"] = "M12 7.5 A4.5 4.5 0 1 0 12 16.5 A4.5 4.5 0 1 0 12 7.5 " +
                  "M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 " +
                  "M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M19.1 4.9 L17.7 6.3 M6.3 17.7 L4.9 19.1",
        ["Moon"] = "M20 14.5 A8.5 8.5 0 0 1 9.5 4 A8.5 8.5 0 1 0 20 14.5 Z",
        ["Auto"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 3 V21",
        ["Palette"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 " +
                      "M8.5 9 A1.2 1.2 0 1 0 8.5 11.4 A1.2 1.2 0 1 0 8.5 9 " +
                      "M12 7.5 A1.2 1.2 0 1 0 12 9.9 A1.2 1.2 0 1 0 12 7.5 " +
                      "M15.5 9 A1.2 1.2 0 1 0 15.5 11.4 A1.2 1.2 0 1 0 15.5 9",
        ["Window"] = "M3 4 H21 V20 H3 Z M3 8 H21",
        ["Clock"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 7 V12 L15.5 14.5",

        // ── 扩展图标 ──────────────────────────────────────────
        ["Code"] = "M16 18 L22 12 L16 6 M8 6 L2 12 L8 18",
        ["Link"] = "M10 13 A5 5 0 0 0 17.07 13 L19.9 10.17 A5 5 0 0 0 12.83 3.1 L11 5 M14 11 A5 5 0 0 0 6.93 11 L4.1 13.83 A5 5 0 0 0 11.17 20.9 L13 19",
        ["Gear"] = "M12 15 A3 3 0 1 0 12 9 A3 3 0 1 0 12 15 Z M19.4 15 A1.65 1.65 0 0 0 19.73 16.82 L20 17.2 A2 2 0 1 1 17.2 20 L16.82 19.73 A1.65 1.65 0 0 0 15 19.4 A1.65 1.65 0 0 0 13.5 20.73 V21.2 A2 2 0 1 1 9.5 21.2 V20.73 A1.65 1.65 0 0 0 8 19.4 A1.65 1.65 0 0 0 6.18 19.73 L5.8 20 A2 2 0 1 1 3 17.2 L3.27 16.82 A1.65 1.65 0 0 0 3.6 15 A1.65 1.65 0 0 0 2.27 13.5 H1.8 A2 2 0 1 1 1.8 9.5 H2.27 A1.65 1.65 0 0 0 3.6 8 A1.65 1.65 0 0 0 3.27 6.18 L3 5.8 A2 2 0 1 1 5.8 3 L6.18 3.27 A1.65 1.65 0 0 0 8 3.6 A1.65 1.65 0 0 0 9.5 2.27 V1.8 A2 2 0 1 1 13.5 1.8 V2.27 A1.65 1.65 0 0 0 15 3.6 A1.65 1.65 0 0 0 16.82 3.27 L17.2 3 A2 2 0 1 1 20 5.8 L19.73 6.18 A1.65 1.65 0 0 0 19.4 8 A1.65 1.65 0 0 0 20.73 9.5 H21.2 A2 2 0 1 1 21.2 13.5 H20.73 A1.65 1.65 0 0 0 19.4 15 Z",
        ["Star"] = "M12 2 L15.09 8.26 L22 9.27 L17 14.14 L18.18 21.02 L12 17.77 L5.82 21.02 L7 14.14 L2 9.27 L8.91 8.26 Z",
        ["Heart"] = "M19 14 C20.5 12.5 21.5 10.5 21.5 8 C21.5 4.5 18.5 2 15 2 C13.5 2 12.5 3 12 3.5 C11.5 3 10.5 2 9 2 C5.5 2 2.5 4.5 2.5 8 C2.5 10.5 3.5 12.5 5 14 L12 21 Z",
        ["Bell"] = "M18 8 A6 6 0 0 0 6 8 C6 15 3 17 3 17 H21 S18 15 18 8 M10.3 21 A2 2 0 0 0 13.7 21",
        ["Pin"] = "M12 17 V22 M5 17 H19 M6 10 C6 6 9 3 12 3 C15 3 18 6 18 10 V17 H6 Z",
        ["Command"] = "M18 9 A3 3 0 1 0 15 6 V18 A3 3 0 1 0 18 15 H6 A3 3 0 1 0 9 18 V6 A3 3 0 1 0 6 9 Z",
        ["Activity"] = "M22 12 H18 L15 21 L9 3 L6 12 H2",
        ["Cpu"] = "M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z M9 1 V4 M15 1 V4 M9 20 V23 M15 20 V23 M20 9 H23 M20 15 H23 M1 9 H4 M1 15 H4",
        ["CheckCircle"] = "M22 11.08 V12 A10 10 0 1 1 16.17 3.23 M22 4 L12 14.01 L9 11.01",

        // ── 窗口标题栏 ────────────────────────────────────────
        ["Minimize"] = "M5 12 H19",
        ["Maximize"] = "M5 5 H19 V19 H5 Z",
        ["Restore"] = "M8 8 V5 H19 V16 H16 M5 8 H16 V19 H5 Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    /// <summary>规范化图标名称（支持 Solid_ 前缀、fa- 前缀、大小写不敏感及常见别名）</summary>
    public static string? NormalizeKey(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        kind = kind.Trim();
        if (Paths.ContainsKey(kind)) return kind;

        var k = kind;
        if (k.StartsWith("Solid_", System.StringComparison.OrdinalIgnoreCase))
            k = k[6..];
        else if (k.StartsWith("fa-", System.StringComparison.OrdinalIgnoreCase))
            k = k[3..];
        else if (k.StartsWith("fa_", System.StringComparison.OrdinalIgnoreCase))
            k = k[3..];

        // 别名映射
        switch (k.ToLowerInvariant())
        {
            case "cog":
            case "settings":
            case "gear":
                return "Gear";
            case "comment":
            case "message":
                return "Message";
            case "desktop":
            case "monitor":
                return "Monitor";
            case "edit":
            case "pencil":
                return "Pencil";
            case "code":
                return "Code";
            case "link":
                return "Link";
            case "star":
                return "Star";
            case "heart":
                return "Heart";
            case "bell":
                return "Bell";
            case "fileimport":
                return "Import";
            case "fileexport":
                return "Export";
        }

        foreach (var key in Paths.Keys)
        {
            if (string.Equals(key, k, System.StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return null;
    }

    /// <summary>取图标几何；未知名称返回 null</summary>
    public static Geometry? Get(string? kind)
    {
        var resolved = NormalizeKey(kind);
        if (resolved is null) return null;
        if (Cache.TryGetValue(resolved, out var cached)) return cached;
        if (!Paths.TryGetValue(resolved, out var data)) return null;
        var geometry = Geometry.Parse(data);
        Cache[resolved] = geometry;
        return geometry;
    }

    /// <summary>该名称是否有内置图标</summary>
    public static bool Has(string? kind)
        => NormalizeKey(kind) is not null;

    /// <summary>全部图标名（调试/自检用）</summary>
    public static IEnumerable<string> AllNames => Paths.Keys;
}
