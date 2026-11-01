using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace QuickLauncher.App.Services;

/// <summary>
/// 开机自启：注册表 HKCU Run 项，由 explorer.exe 以「中完整性」启动。
///
/// 为什么不用计划任务（重要，勿改回）：
/// 本程序 manifest 为 asInvoker + UIAccess(uiAccess=true)，而 Windows 只允许
/// 「中完整性」进程启动 UIAccess 程序。Task Scheduler 服务自身以 LocalSystem
/// （高完整性）运行，schtasks 的 /RL LIMITED 仅降权【目标进程】、并不改变
/// Task Scheduler 作为父进程的身份，因此由它启动本程序会被系统拒绝并返回
/// 740(ERROR_ELEVATION_REQUIRED)，表现为"计划任务明明就绪却静默不启动"。
/// 实测：Start-ScheduledTask 触发 → LastTaskResult 0x800702E4，进程未创建。
///
/// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run 由 explorer.exe（中完整性
/// 桌面外壳）在用户登录后启动，符合 UIAccess 启动模型，这也是 Quicker 等同类
/// UIAccess 软件的自启方式。
/// </summary>
public static class StartupService
{
    /// <summary>Run 项值名（同时用于旧版计划任务名的清理标识）。</summary>
    public const string RunValueName = "QuickLauncher";

    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    /// <summary>设置/取消开机自启（注册表 Run 项），并一次性清理旧版计划任务。</summary>
    public static (bool ok, string message) Apply(bool enable)
    {
        // 无论启停都清理一次旧版 schtasks 计划任务（Run 项与计划任务不可并存，
        // 否则两处都会尝试启动，靠单实例互斥量兜底但会互相唤醒/退出，行为混乱）。
        RemoveLegacyTask();

        if (!enable) return DeleteRunKey();
        return SetRunKey();
    }

    /// <summary>当前是否已配置开机自启（Run 项存在）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 构造注册表 Run 项的值。
    /// 路径含空格（Program Files）必须整体加引号，否则 explorer 启动时会在第一个空格处截断；
    /// 追加 --autostart 让开机启动走「静默启动」（只驻留托盘，不弹主窗口）。
    /// </summary>
    public static string BuildRunValue(string exePath) => $"\"{exePath}\" {StartupArgs.Autostart}";

    private static (bool ok, string message) SetRunKey()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return (false, "无法定位程序路径");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return (false, "无法打开开机自启注册表项");

            key.SetValue(RunValueName, BuildRunValue(exe), RegistryValueKind.String);
            return (true, "已开启开机自启（登录时静默启动，由资源管理器拉起，配合 UIAccess 可操作管理员窗口）");
        }
        catch (Exception ex)
        {
            return (false, $"开启开机自启失败：{ex.Message}");
        }
    }

    private static (bool ok, string message) DeleteRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            return (true, "已关闭开机自启");
        }
        catch (Exception ex)
        {
            return (false, $"关闭开机自启失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 删除旧版用 schtasks 创建的计划任务（一次性迁移）。
    /// 旧任务留着不会生效（UIAccess + 计划任务必然 740），但会在任务计划程序里
    /// 留下一条永远失败的历史记录，故显式清掉。
    /// </summary>
    private static void RemoveLegacyTask()
    {
        try
        {
            Run("schtasks", $"/Delete /TN \"{RunValueName}\" /F");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy task cleanup failed: {ex.Message}");
        }
    }

    private static (int code, string output) Run(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outp);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
