using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// 窗口步骤：激活、查找、等待、信息
/// </summary>
public static class WindowSteps
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static (IntPtr hwnd, string title, string className, int pid, string processName) GetForegroundInfo()
    {
        var h = GetForegroundWindow();
        return GetInfo(h);
    }

    public static (IntPtr hwnd, string title, string className, int pid, string processName) GetInfo(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        var cls = new StringBuilder(256);
        GetClassNameW(h, cls, cls.Capacity);
        GetWindowThreadProcessId(h, out var pid);
        string proc = "";
        try { using var p = Process.GetProcessById(pid); proc = p.ProcessName; } catch { }
        return (h, sb.ToString(), cls.ToString(), pid, proc);
    }

    public static List<(IntPtr hwnd, string title, string processName)> FindWindows(string? processName = null, string? titleMatch = null)
    {
        var results = new List<(IntPtr, string, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrEmpty(title)) return true;
            GetWindowThreadProcessId(h, out var pid);
            string proc = "";
            try { using var p = Process.GetProcessById(pid); proc = p.ProcessName; } catch { return true; }
            if (processName is not null && !WildcardMatch(proc, processName)) return true;
            if (titleMatch is not null && !WildcardMatch(title, titleMatch)) return true;
            results.Add((h, title, proc));
            return true;
        }, IntPtr.Zero);
        return results;
    }

    public static void Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9);
        SetForegroundWindow(hWnd);
    }

    public static bool WildcardMatch(string input, string pattern)
    {
        // 简单通配：* 任意多字符 ? 任意单字符
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public sealed class GetForegroundWindowInfo : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "GetForegroundWindowInfo",
            DisplayName = "获取前台窗口信息",
            Category = "系统",
            Params =
            {
                new StepParamDef { Name = "titleVar", DisplayName = "标题变量名", DefaultValue = "windowTitle" },
                new StepParamDef { Name = "procVar", DisplayName = "进程变量名", DefaultValue = "windowProcess" },
                new StepParamDef { Name = "classVar", DisplayName = "类名变量名", DefaultValue = "windowClass" },
                new StepParamDef { Name = "hwndVar", DisplayName = "句柄变量名", DefaultValue = "windowHwnd" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var info = GetForegroundInfo();
            ctx.Set(Or(ctx, step, "titleVar", "windowTitle"), info.title);
            ctx.Set(Or(ctx, step, "procVar", "windowProcess"), info.processName);
            ctx.Set(Or(ctx, step, "classVar", "windowClass"), info.className);
            ctx.Set(Or(ctx, step, "hwndVar", "windowHwnd"), info.hwnd.ToInt64());
            return StepResult.Ok();
        }

        private static string Or(StepContext ctx, ActionStep step, string key, string fallback)
        {
            var v = ctx.Interpolate(step.Inputs.GetValueOrDefault(key));
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }

    public sealed class ActivateWindow : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "ActivateWindow",
            DisplayName = "激活窗口",
            Category = "界面交互",
            Params =
            {
                new StepParamDef { Name = "process", DisplayName = "进程名（支持通配）" },
                new StepParamDef { Name = "title", DisplayName = "窗口标题（支持通配）" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var proc = ctx.Interpolate(step.Inputs.GetValueOrDefault("process"));
            var title = ctx.Interpolate(step.Inputs.GetValueOrDefault("title"));
            var windows = FindWindows(
                string.IsNullOrEmpty(proc) ? null : proc,
                string.IsNullOrEmpty(title) ? null : title);
            if (windows.Count == 0) return StepResult.Fail("未找到匹配窗口");
            Activate(windows[0].hwnd);
            return StepResult.Ok();
        }
    }

    public sealed class WaitWindow : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "WaitWindow",
            DisplayName = "等待窗口",
            Category = "界面交互",
            Params =
            {
                new StepParamDef { Name = "process", DisplayName = "进程名（支持通配）" },
                new StepParamDef { Name = "title", DisplayName = "窗口标题（支持通配）" },
                new StepParamDef
                {
                    Name = "waitType", DisplayName = "等待类型",
                    Options = new[] { "appear", "disappear" }, DefaultValue = "appear"
                },
                new StepParamDef { Name = "timeoutSec", DisplayName = "超时(秒)", Type = VariableType.Number, DefaultValue = "10" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var proc = ctx.Interpolate(step.Inputs.GetValueOrDefault("process"));
            var title = ctx.Interpolate(step.Inputs.GetValueOrDefault("title"));
            var waitType = ctx.Interpolate(step.Inputs.GetValueOrDefault("waitType"));
            if (!double.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("timeoutSec")), out var timeout))
                timeout = 10;

            var deadline = DateTime.UtcNow.AddSeconds(timeout);
            bool Found() => FindWindows(
                string.IsNullOrEmpty(proc) ? null : proc,
                string.IsNullOrEmpty(title) ? null : title).Count > 0;

            while (DateTime.UtcNow < deadline)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                var found = Found();
                if (waitType == "appear" && found) return StepResult.Ok();
                if (waitType == "disappear" && !found) return StepResult.Ok();
                Thread.Sleep(200);
            }
            return StepResult.Fail($"等待窗口超时: {waitType}");
        }
    }
}
