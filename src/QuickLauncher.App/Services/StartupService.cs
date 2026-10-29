using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace QuickLauncher.App.Services;

/// <summary>
/// 开机自启：Windows 计划任务（登录时·普通权限 LIMITED）静默启动，替代旧的注册表 Run 项。
/// 权限模型已由 requireAdministrator 改为 asInvoker + UIAccess（见 app.manifest）：
/// 程序以「中完整性 + UIAccess 特权」运行即可操作管理员窗口的 UI，无需最高权限，
/// 因此计划任务也用 /RL LIMITED（登录时以普通权限启动，不弹 UAC、不静默提权）。
/// </summary>
public static class StartupService
{
    public const string TaskName = "QuickLauncher";
    private const string RunValueName = "QuickLauncher";

    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    /// <summary>设置/取消开机自启（计划任务·登录时·最高权限），并清理旧的注册表 Run 项。</summary>
    public static (bool ok, string message) Apply(bool enable)
    {
        // 无论启停都清理一次旧的 Run 项（一次性迁移，防止两处并存）
        RemoveLegacyRunKey();

        if (!enable) return DeleteTask();
        // 直接以 /Create /F 覆盖创建，确保 /RL 运行级别为 LIMITED，
        // 可把第 1 步遗留的 HIGHEST 旧任务一次性纠正为「普通权限 + UIAccess」。
        return CreateTask();
    }

    /// <summary>当前是否已配置开机自启（计划任务存在）。</summary>
    public static bool IsEnabled()
    {
        var (code, _) = Run("schtasks", $"/Query /TN \"{TaskName}\"");
        return code == 0;
    }

    private static (bool ok, string message) CreateTask()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return (false, "无法定位程序路径");

        // /SC ONLOGON 登录时触发；/RL LIMITED 普通权限（配合 UIAccess 操作管理员窗口 UI，无需最高权限）；/F 覆盖同名任务。
        // /TR 的路径含空格，用引号包裹。ExePath 取 Environment.ProcessPath：部署到 Program Files 后自动指向安装路径。
        var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL LIMITED /F";
        var (code, output) = Run("schtasks", args);
        return code == 0
            ? (true, "已开启开机自启（计划任务·登录时·普通权限 LIMITED，配合 UIAccess）")
            : (false, $"创建计划任务失败：{output.Trim()}");
    }

    private static (bool ok, string message) DeleteTask()
    {
        Run("schtasks", $"/Delete /TN \"{TaskName}\" /F");
        // 任务不存在时 schtasks 也返回非 0，统一视为“已关闭”
        return (true, "已关闭开机自启");
    }

    /// <summary>删除旧版写入的注册表 Run 项（迁移清理）。</summary>
    private static void RemoveLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 忽略注册表访问失败
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
