using System;
using System.Linq;

namespace QuickLauncher.App;

/// <summary>
/// 启动参数解析。
///
/// 开机自启时需要「静默启动」：只驻留托盘、不弹主窗口（否则每次登录都会弹一个
/// 管理窗口盖在用户面前）。自启项通过注册表 Run 启动，参数由 StartupService 写入。
/// </summary>
public static class StartupArgs
{
    /// <summary>开机自启（静默）</summary>
    public const string Autostart = "--autostart";

    /// <summary>静默启动（别名）</summary>
    public const string Silent = "--silent";

    /// <summary>静默启动（别名）</summary>
    public const string Hidden = "--hidden";

    /// <summary>
    /// 是否为静默启动：启动后只驻留托盘，不显示主窗口。
    /// 用户仍可从托盘图标双击打开主窗口。
    /// </summary>
    public static bool IsSilent(string[]? args)
    {
        if (args is null || args.Length == 0) return false;
        return args.Any(a =>
            a.Equals(Autostart, StringComparison.OrdinalIgnoreCase) ||
            a.Equals(Silent, StringComparison.OrdinalIgnoreCase) ||
            a.Equals(Hidden, StringComparison.OrdinalIgnoreCase));
    }
}
