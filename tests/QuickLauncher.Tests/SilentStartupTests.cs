using System;
using Xunit;

namespace QuickLauncher.Tests;

/// <summary>
/// 静默启动（开机自启）回归测试。
///
/// 背景：开机自启时如果直接 Show 主窗口，用户每次登录都会看到一个管理窗口盖在面前
/// （实测截图确认）。修复方式是自启项带 --autostart 参数，启动时不显示主窗口，
/// 只驻留托盘，用户可从托盘双击打开。
/// </summary>
public class SilentStartupTests
{
    [Theory]
    [InlineData("--autostart", true)]
    [InlineData("--AUTOSTART", true)]
    [InlineData("--silent", true)]
    [InlineData("--hidden", true)]
    [InlineData("--run", false)]
    [InlineData("--autostart2", false)]
    [InlineData("", false)]
    public void IsSilent_RecognizesFlag(string arg, bool expected)
    {
        Assert.Equal(expected, QuickLauncher.App.StartupArgs.IsSilent(new[] { arg }));
    }

    [Fact]
    public void IsSilent_NoArgs_IsFalse()
    {
        Assert.False(QuickLauncher.App.StartupArgs.IsSilent(null));
        Assert.False(QuickLauncher.App.StartupArgs.IsSilent(Array.Empty<string>()));
    }

    [Fact]
    public void IsSilent_FlagAmongOtherArgs_IsTrue()
    {
        // 文本指令/其它参数与自启参数并存时仍应识别为静默
        Assert.True(QuickLauncher.App.StartupArgs.IsSilent(new[] { "--run", "--autostart" }));
    }

    [Fact]
    public void BuildRunValue_QuotesPathAndAppendsAutostartFlag()
    {
        var value = QuickLauncher.App.Services.StartupService.BuildRunValue(
            @"C:\Program Files\QuickLauncher\QuickLauncher.App.exe");

        // 路径含空格必须整体加引号，否则 explorer 会在第一个空格处截断
        Assert.StartsWith("\"C:\\Program Files\\QuickLauncher\\QuickLauncher.App.exe\"", value);
        // 必须带 --autostart，否则开机自启会弹出主窗口
        Assert.Contains(QuickLauncher.App.StartupArgs.Autostart, value);
    }

    [Fact]
    public void BuildRunValue_FlagIsSeparateArgument()
    {
        var value = QuickLauncher.App.Services.StartupService.BuildRunValue(@"C:\a\b.exe");
        // 参数必须与路径用空格分隔（不能粘在引号里），否则进程收不到该参数
        Assert.EndsWith(" " + QuickLauncher.App.StartupArgs.Autostart, value);
        Assert.DoesNotContain("\"" + QuickLauncher.App.StartupArgs.Autostart, value);
    }
}
