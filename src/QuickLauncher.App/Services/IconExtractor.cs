using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLauncher.App.Services;

/// <summary>
/// 应用图标提取器：从 exe/dll/icl/ico 提取内嵌图标，缓存为 PNG（对应原版 Quicker 的 ImageCache 机制）。
/// Icon 字段存 PNG 文件路径，渲染层按图片显示。
/// </summary>
public static class IconExtractor
{
    [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string fileName, int iconIndex,
        IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint iconCount);

    [DllImport("User32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static string IconDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickLauncher", "icons");

    /// <summary>解析 .lnk 快捷方式的目标路径（开始菜单程序多为 .lnk）</summary>
    public static string? ResolveShortcut(string lnkPath)
    {
        if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath)) return null;
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return null;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            var target = shortcut.TargetPath as string;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>提取前的路径规整：.lnk 解析到目标 exe</summary>
    private static string? ResolvePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return ResolveShortcut(filePath) ?? filePath;
        return filePath;
    }

    /// <summary>枚举文件内图标数量（exe/dll/icl）</summary>
    public static int CountIcons(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;
        filePath = ResolvePath(filePath)!;
        try
        {
            return (int)ExtractIconEx(filePath, -1, null, null, 0);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>提取指定索引的图标（原尺寸，渲染时由界面缩放）</summary>
    public static BitmapSource? Extract(string filePath, int iconIndex)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        filePath = ResolvePath(filePath)!;
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        uint got;
        try
        {
            got = ExtractIconEx(filePath, iconIndex, large, small, 1);
        }
        catch
        {
            return null;
        }
        try
        {
            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (got == 0 || handle == IntPtr.Zero) return null;
            return Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }

    /// <summary>把图标保存为 PNG 到图标缓存目录，返回文件路径（已存在则直接命中缓存）</summary>
    public static string? SaveAsPng(BitmapSource icon, string cacheKey)
    {
        try
        {
            Directory.CreateDirectory(IconDir);
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(cacheKey));
            var name = Convert.ToHexString(hash)[..16] + ".png";
            var path = Path.Combine(IconDir, name);
            if (!File.Exists(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));
                using var fs = File.Create(path);
                encoder.Save(fs);
            }
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>一步提取并缓存：返回 PNG 路径（失败 null）</summary>
    public static string? ExtractAndCache(string filePath, int iconIndex)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        var resolved = ResolvePath(filePath)!;
        var icon = Extract(resolved, iconIndex);
        if (icon is null) return null;
        return SaveAsPng(icon, $"{resolved.ToLowerInvariant()}#{iconIndex}");
    }

    /// <summary>当前图标描述是否为可显示的图片文件</summary>
    public static bool IsImageFile(string? iconDesc)
    {
        if (string.IsNullOrEmpty(iconDesc) || iconDesc.Length <= 2 || iconDesc.StartsWith("["))
            return false;
        try
        {
            return File.Exists(iconDesc);
        }
        catch
        {
            return false;
        }
    }
}
