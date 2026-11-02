using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MakeIcon;

/// <summary>
/// 「速动」应用图标生成器。
///
/// 设计要点（相对旧版的修正）：
///   1. 主体图形严格内缩，四周留出足够安全边距 —— 旧版闪电撑满整个圆角方块、
///      被圆角裁切后变成「白色斜杠把方块劈成两半」，小尺寸直接糊掉。
///   2. 主体用「亮白 → 淡蓝」的微渐变 + 柔和辉光 + 斜下投影，替代纯平白，
///      在深色任务栏和浅色背景上都能立住。
///   3. 圆角方形用 22.5% 圆角 + 内侧 1px 高光边，模拟玻璃/陶瓷质感。
///   4. 渐变与 UI 设计系统完全一致：青蓝 #38BDF8 → 蓝 #4F6BFF → 紫 #8B5CF6。
/// </summary>
internal static class Program
{
    // ── 品牌色（与 App.xaml 的 Brush.Gradient 保持一致）──────────────
    private static readonly Color C1 = Color.FromRgb(0x38, 0xBD, 0xF8);
    private static readonly Color C2 = Color.FromRgb(0x4F, 0x6B, 0xFF);
    private static readonly Color C3 = Color.FromRgb(0x8B, 0x5C, 0xF6);

    private const int Canvas = 24;          // 逻辑画布边长（与图标库同一坐标系）
    private const int LogoSize = 128;       // logo.png 的输出边长（界面内显示 24~34 DIP，兼顾高分屏）

    [STAThread]
    private static int Main(string[] args)
    {
        string variant = "all";
        string? outDir = null;
        string? previewDir = null;
        string? source = null;
        string label = "import";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
                arg = arg[2..];

            var parts = arg.Split('=', 2);
            switch (parts[0])
            {
                case "variant": variant = parts.Length > 1 ? parts[1] : "all"; break;
                case "out": outDir = parts.Length > 1 ? parts[1] : null; break;
                case "preview": previewDir = parts.Length > 1 ? parts[1] : null; break;
                case "source": source = parts.Length > 1 ? parts[1] : null; break;
                case "label": label = parts.Length > 1 ? parts[1] : "import"; break;
                default:
                    Console.WriteLine($"未知参数：{args[i]}");
                    Console.WriteLine("用法：variant=<bolt|trail|knockout|all|import> out=<目录> preview=<目录> source=<图片路径>");
                    return 1;
            }
        }

        var variants = variant == "all"
            ? new[] { "bolt", "trail", "knockout" }
            : variant.Split(',');

        // 导入模式：直接使用一张现成图片作为应用图标
        if (variant == "import" || variant.Contains("import"))
        {
            // 未指定源图时，默认取仓库内的图标原稿
            source ??= Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
                    AppContext.BaseDirectory))) ?? ".", "art", "sudong-icon.png");
            if (!File.Exists(source))
            {
                Console.WriteLine($"找不到源图：{source}");
                return 1;
            }
            var artwork = ImportArtwork(source);
            if (previewDir is not null)
            {
                Directory.CreateDirectory(previewDir);
                WritePng(artwork, Path.Combine(previewDir, $"cand-{label}.png"));
                WriteSheet(new[] { label }, previewDir,
                    new Dictionary<string, BitmapSource> { [label] = artwork });
                Console.WriteLine("预览已输出 -> " + previewDir);
            }
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                // 完整显示：不裁剪任何内容，各尺寸一律等比缩放到满幅
                // logo.png 用面积平均降到 64px —— 界面内只显示 22~32 DIP，
                // 运行时再缩放时倍率很小，就不会出现色块
                WritePng(ResizeAreaAverage(artwork, LogoSize), Path.Combine(outDir, "logo.png"));
                WriteIco(outDir, s => ResizeAreaAverage(artwork, s));
                Console.WriteLine($"OK [import] -> {outDir}");
            }
            return 0;
        }

        if (previewDir is not null)
        {
            Directory.CreateDirectory(previewDir);
            foreach (var v in variants) WritePng(Render(v, 256), Path.Combine(previewDir, $"cand-{v}.png"));
            WriteSheet(variants, previewDir);
            Console.WriteLine("预览已输出 -> " + previewDir);
        }

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            foreach (var v in variants)
            {
                WritePng(Render(v, 256), Path.Combine(outDir, "logo.png"));
                WriteIco(v, outDir);
                Console.WriteLine($"OK [{v}] -> {outDir}");
            }
        }

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // 渲染
    // ═══════════════════════════════════════════════════════════════

    private static BitmapSource Render(string variant, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var scale = size / (double)Canvas;
            dc.PushTransform(new ScaleTransform(scale, scale));

            switch (variant)
            {
                case "trail": DrawTrail(dc); break;
                case "knockout": DrawKnockout(dc); break;
                default: DrawBolt(dc); break;
            }

            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>圆角方形底板（渐变 + 左上高光 + 内侧亮边）</summary>
    private static RectangleGeometry RoundedSquare(double inset = 0)
    {
        var r = Math.Max(0, Canvas * 0.225 - inset);
        return new RectangleGeometry(new Rect(inset, inset, Canvas - inset * 2, Canvas - inset * 2), r, r);
    }

    private static LinearGradientBrush BrandGradient()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(C1, 0));
        brush.GradientStops.Add(new GradientStop(C2, 0.5));
        brush.GradientStops.Add(new GradientStop(C3, 1));
        return brush;
    }

    private static void DrawPlate(DrawingContext dc, Geometry plate)
    {
        dc.DrawGeometry(BrandGradient(), null, plate);

        // 左上径向高光：提升玻璃/陶瓷质感
        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.30, 0.22),
            Center = new Point(0.30, 0.22),
            RadiusX = 0.62,
            RadiusY = 0.62,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(78, 255, 255, 255), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        dc.DrawRectangle(glow, null, new Rect(0, 0, Canvas, Canvas));

        // 右下轻微压暗，增加体积感
        var shade = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.78, 0.82),
            Center = new Point(0.78, 0.82),
            RadiusX = 0.7,
            RadiusY = 0.7,
        };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(60, 22, 12, 60), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 22, 12, 60), 1));
        dc.DrawRectangle(shade, null, new Rect(0, 0, Canvas, Canvas));

        // 内侧 1px 高光边
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(62, 255, 255, 255)), 0.16);
        dc.DrawGeometry(null, edge, RoundedSquare(0.14));
    }

    /// <summary>主体：闪电多边形（内缩到安全边距内，不再被圆角裁切）</summary>
    private static Geometry BoltGeometry() => BuildPolygon(new[]
    {
        new Point(15.0, 4.8),
        new Point(5.4, 15.0),
        new Point(10.6, 15.0),
        new Point(9.0, 19.2),
        new Point(18.6, 9.0),
        new Point(13.2, 9.0),
    });

    private static Geometry BuildPolygon(Point[] points)
    {
        var geo = new PathGeometry();
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        for (var i = 1; i < points.Length; i++)
            figure.Segments.Add(new LineSegment(points[i], true));
        geo.Figures.Add(figure);
        geo.Freeze();
        return geo;
    }

    /// <summary>主体描边：亮白微渐变 + 柔和辉光 + 斜下投影</summary>
    private static void DrawMark(DrawingContext dc, Geometry mark)
    {
        // 斜下柔影：多层放大副本叠加，模拟模糊投影（直接画硬边副本会像「重影」）
        for (var i = 6; i >= 1; i--)
        {
            var t = i / 6.0;
            var grow = 1.0 + 0.16 * t;
            var alpha = (byte)Math.Round(10 + 30 * (1 - t));
            dc.PushTransform(new TransformGroup
            {
                Children =
                {
                    new TranslateTransform(0.30 * t, 0.46 * t),
                    new TranslateTransform(Canvas * (1 - grow) / 2, Canvas * (1 - grow) / 2),
                    new ScaleTransform(grow, grow),
                }
            });
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, 16, 10, 44)), null, mark);
            dc.Pop();
        }

        // 外发光：几层同心放大的淡白副本
        for (var i = 8; i >= 1; i--)
        {
            var t = i / 8.0;
            var grow = 1.0 + 0.38 * t;
            var alpha = (byte)Math.Round(4 + 22 * (1 - t));
            dc.PushTransform(new TransformGroup
            {
                Children =
                {
                    new TranslateTransform(Canvas * (1 - grow) / 2, Canvas * (1 - grow) / 2),
                    new ScaleTransform(grow, grow),
                }
            });
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), null, mark);
            dc.Pop();
        }

        // 主体：白 → 淡蓝的竖向微渐变
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        fill.GradientStops.Add(new GradientStop(Colors.White, 0));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xEE, 0xFF), 1));
        dc.DrawGeometry(fill, null, mark);
    }

    /// <summary>方案 A：精修闪电（推荐）</summary>
    private static void DrawBolt(DrawingContext dc)
    {
        DrawPlate(dc, RoundedSquare());
        DrawMark(dc, BoltGeometry());
    }

    /// <summary>方案 B：闪电残影（同一枚闪电向右上方加速拖尾，兼顾「闪电」识别与速度感）</summary>
    private static void DrawTrail(DrawingContext dc)
    {
        DrawPlate(dc, RoundedSquare());

        var bolt = new[]
        {
            new Point(15.0, 4.8),
            new Point(5.4, 15.0),
            new Point(10.6, 15.0),
            new Point(9.0, 19.2),
            new Point(18.6, 9.0),
            new Point(13.2, 9.0),
        };

        // 从左下到右上三段递减：尾 → 中 → 主标
        var trail = new (double Scale, double Dx, double Dy, byte Alpha)[]
        {
            (0.58, -2.35, 2.55, 40),
            (0.80, -1.05, 1.15, 105),
            (1.00, 0.00, 0.00, 255),
        };

        foreach (var (scale, dx, dy, alpha) in trail)
        {
            var geo = TransformPolygon(bolt, scale, dx, dy);
            dc.PushTransform(new TransformGroup
            {
                Children = { new TranslateTransform(dx, dy) }
            });
            dc.Pop();

            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0xE8, 0xEE, 0xFF), 1));
            dc.DrawGeometry(fill, null, geo);
        }
    }

    /// <summary>以画布中心为基准缩放并平移多边形</summary>
    private static Geometry TransformPolygon(Point[] points, double scale, double dx, double dy)
    {
        var moved = points.Select(p => new Point(
            12 + (p.X - 12) * scale + dx,
            12 + (p.Y - 12) * scale + dy)).ToArray();
        return BuildPolygon(moved);
    }

    /// <summary>方案 C：负形闪电（扁平现代，闪电是底板挖出来的空洞）</summary>
    private static void DrawKnockout(DrawingContext dc)
    {
        var plate = RoundedSquare();
        var bolt = BoltGeometry();

        // EvenOdd 合并：闪电区域被挖空
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude, plate, bolt);

        dc.DrawGeometry(BrandGradient(), null, combined);

        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.30, 0.22),
            Center = new Point(0.30, 0.22),
            RadiusX = 0.62,
            RadiusY = 0.62,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(66, 255, 255, 255), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, plate, bolt));
        dc.DrawRectangle(glow, null, new Rect(0, 0, Canvas, Canvas));
        dc.Pop();

        var edge = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 0.16);
        dc.DrawGeometry(null, edge, RoundedSquare(0.14));
    }

    /// <summary>
    /// 导入一张现成图片作为应用图标：
    /// 自动识别「圆角方块」的实际边界 → 裁掉四周纯黑留白 → 补出透明圆角 → 输出多尺寸 ICO。
    ///
    /// 【为什么需要】用户提供的原图常是无 Alpha 的位图，四周是纯黑留白。
    /// 直接当图标用，任务栏/托盘里会显示成一个黑方块；必须自己抠出圆角并补透明。
    /// </summary>
    private static BitmapSource ImportArtwork(string path)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.UriSource = new Uri(Path.GetFullPath(path));
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.EndInit();
        src.Freeze();

        var w = src.PixelWidth;
        var h = src.PixelHeight;

        // 统一转成 Bgra32（源图可能是 24bpp 无 Alpha），再直读像素做边界分析
        var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var wb = new WriteableBitmap(bgra);
        var stride = wb.BackBufferStride;
        var px = new byte[stride * wb.PixelHeight];
        wb.Lock();
        try { Marshal.Copy(wb.BackBuffer, px, 0, px.Length); }
        finally { wb.Unlock(); }

        // ── 1. 找内容边界（近黑视为图外留白）────────────────────
        const byte threshold = 26;
        var minX = w; var minY = h; var maxX = -1; var maxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            for (var x = 0; x < w; x++)
            {
                var i = row + x * 4;
                if (px[i] > threshold || px[i + 1] > threshold || px[i + 2] > threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < 0) throw new InvalidOperationException("图片没有可识别的内容");

        // ── 2. 取正方形区域（以内容中心对齐）────────────────────
        var side = Math.Min(Math.Max(maxX - minX + 1, maxY - minY + 1), Math.Min(w, h));
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var sx = Math.Clamp(cx - side / 2, 0, w - side);
        var sy = Math.Clamp(cy - side / 2, 0, h - side);

        // ── 3. 探测圆角半径（沿对角线找到第一个「有内容」的偏移）────
        var radius = 0;
        for (var d = 0; d < side / 3; d++)
        {
            var i = (sy + d) * stride + (sx + d) * 4;
            if (px[i] > threshold || px[i + 1] > threshold || px[i + 2] > threshold) { radius = d; break; }
        }
        radius = Math.Clamp(radius, 0, side / 2);
        Console.WriteLine($"  内容边界: x[{minX}..{maxX}] y[{minY}..{maxY}]  取正方形 {side}px @ ({sx},{sy})  圆角半径 ≈ {radius}px");

        // ── 4. 裁剪 + 生成带透明圆角的位图 ─────────────────────
        var outPx = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            var srcRow = (sy + y) * stride;
            for (var x = 0; x < side; x++)
            {
                var si = srcRow + (sx + x) * 4;
                var di = (y * side + x) * 4;
                outPx[di] = px[si];
                outPx[di + 1] = px[si + 1];
                outPx[di + 2] = px[si + 2];
                outPx[di + 3] = InRoundedRect(x, y, side, radius) ? (byte)255 : (byte)0;
            }
        }

        var result = new WriteableBitmap(side, side, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, side, side), outPx, side * 4, 0);
        result.Freeze();
        return result;
    }

    /// <summary>点是否落在圆角矩形内</summary>
    private static bool InRoundedRect(int x, int y, int size, int r)
    {
        if (r <= 0) return true;
        int ccx, ccy;
        if (x < r && y < r) { ccx = r; ccy = r; }
        else if (x >= size - r && y < r) { ccx = size - r - 1; ccy = r; }
        else if (x < r && y >= size - r) { ccx = r; ccy = size - r - 1; }
        else if (x >= size - r && y >= size - r) { ccx = size - r - 1; ccy = size - r - 1; }
        else return true;
        var dx = x - ccx;
        var dy = y - ccy;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>
    /// 面积平均降采样（box filter）。
    ///
    /// 【为什么不用 WPF 自带缩放】从 954px 缩到 16px 是 60 倍的极端缩小。
    /// 常规双线性/双三次缩放只做点采样或局部插值，大倍数缩小后会留下
    /// 硬边色块 —— 也就是「马赛克」。面积平均把每个目标像素对应的那一整片
    /// 源像素全部求均值，得到的是真实的平均色，边界平滑、没有色块。
    /// </summary>
    private static BitmapSource ResizeAreaAverage(BitmapSource source, int size)
    {
        var sw = source.PixelWidth;
        var sh = source.PixelHeight;

        var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var wb = new WriteableBitmap(conv);
        var stride = wb.BackBufferStride;
        var src = new byte[stride * wb.PixelHeight];
        wb.Lock();
        try { Marshal.Copy(wb.BackBuffer, src, 0, src.Length); }
        finally { wb.Unlock(); }

        var dst = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var y0 = (int)((long)y * sh / size);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sh / size));
            for (var x = 0; x < size; x++)
            {
                var x0 = (int)((long)x * sw / size);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * sw / size));

                long b = 0, g = 0, r = 0, a = 0;
                var n = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    var row = yy * stride;
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var i = row + xx * 4;
                        b += src[i]; g += src[i + 1]; r += src[i + 2]; a += src[i + 3];
                        n++;
                    }
                }
                var o = (y * size + x) * 4;
                dst[o] = (byte)(b / n);
                dst[o + 1] = (byte)(g / n);
                dst[o + 2] = (byte)(r / n);
                dst[o + 3] = (byte)(a / n);
            }
        }

        var outWb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        outWb.WritePixels(new Int32Rect(0, 0, size, size), dst, size * 4, 0);
        outWb.Freeze();
        return outWb;
    }

    /// <summary>高质量缩放（保留 Alpha）</summary>
    private static BitmapSource Resize(BitmapSource source, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect(0, 0, size, size));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void WriteIcoFrom(BitmapSource artwork, string outDir)
        => WriteIco(outDir, s => Resize(artwork, s));

    // ═══════════════════════════════════════════════════════════════
    // 输出
    // ═══════════════════════════════════════════════════════════════

    private static void WritePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>写多尺寸 ICO（Vista+ 支持 PNG 内嵌）</summary>
    private static void WriteIco(string variant, string outDir)
        => WriteIco(outDir, s => Render(variant, s));

    private static void WriteIco(string outDir, Func<int, BitmapSource> render)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var payloads = sizes.Select(s => EncodePng(render(s))).ToArray();

        using var fs = File.Create(Path.Combine(outDir, "app.ico"));
        using var w = new BinaryWriter(fs);

        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type = icon
        w.Write((ushort)sizes.Length);

        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));  // width
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));  // height
            w.Write((byte)0);           // palette
            w.Write((byte)0);           // reserved
            w.Write((ushort)1);         // color planes
            w.Write((ushort)32);        // bits per pixel
            w.Write(payloads[i].Length);
            w.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var p in payloads) w.Write(p);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>生成对比图（每方案：256 / 64 / 32 / 16 四档）</summary>
    private static void WriteSheet(IReadOnlyList<string> variants, string dir)
    {
        var map = new Dictionary<string, BitmapSource>();
        foreach (var v in variants) map[v] = Render(v, 256);
        WriteSheet(variants, dir, map);
    }

    private static void WriteSheet(IReadOnlyList<string> variants, string dir,
        IReadOnlyDictionary<string, BitmapSource> artwork)
    {
        const int cell = 520;
        const int strip = 300;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1F)), null,
                new Rect(0, 0, cell * variants.Count, strip));

            for (var i = 0; i < variants.Count; i++)
            {
                var x = i * cell;
                var v = variants[i];

                dc.DrawImage(artwork[v], new Rect(x + 20, 22, 256, 256));

                // 小尺寸阶梯排布在右侧，直观看出 16px 的可读性
                var sizes = new[] { 64, 32, 16 };
                var offsetY = 40;
                foreach (var s in sizes)
                {
                    dc.DrawImage(ResizeAreaAverage(artwork[v], s), new Rect(x + 300, offsetY, s, s));
                    offsetY += s + 26;
                }

                var label = new FormattedText(v, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18,
                    Brushes.White, 1.0) { TextAlignment = TextAlignment.Center };
                dc.DrawText(label, new Point(x, 6));
            }
        }

        var rtb = new RenderTargetBitmap(cell * variants.Count, strip, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        WritePng(rtb, Path.Combine(dir, "sheet.png"));
    }
}
