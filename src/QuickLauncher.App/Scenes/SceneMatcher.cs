using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using QuickLauncher.Models;

namespace QuickLauncher.App.Scenes;

/// <summary>
/// 场景匹配：按前台窗口找到应生效的场景（程序场景优先，回落全局）。
/// </summary>
public sealed class SceneMatcher
{
    private readonly List<Scene> _scenes;

    public SceneMatcher(IEnumerable<Scene> scenes)
    {
        _scenes = scenes.Where(s => s.Enabled).ToList();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    public static (string process, string title, string className) GetForegroundInfo()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "", "");
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        var cls = new StringBuilder(256);
        GetClassNameW(h, cls, cls.Capacity);
        GetWindowThreadProcessId(h, out var pid);
        string proc = "";
        try { using var p = Process.GetProcessById(pid); proc = p.ProcessName; } catch { }
        return (proc, sb.ToString(), cls.ToString());
    }

    /// <summary>
    /// 取“稳定”的前台窗口信息。关闭/切换应用的瞬间，GetForegroundWindow 可能返回
    /// 正在销毁的窗口、瞬态 broker 窗口（进程名漂移，如 OmApSvcBroker）或空句柄，
    /// 使下区串到错误场景（或显示“当前程序”）。这里连续采样：直到前台进程与窗口类名
    /// 连续两次一致且进程名非空，最多等待 maxWaitMs；仍不稳定则返回最后一次采样。
    /// </summary>
    public static (string process, string title, string className) GetStableForegroundInfo(int maxWaitMs = 250)
    {
        var prev = GetForegroundInfo();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxWaitMs)
        {
            System.Threading.Thread.Sleep(50);
            var cur = GetForegroundInfo();
            if (!string.IsNullOrEmpty(cur.process)
                && string.Equals(cur.process, prev.process, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur.className, prev.className, StringComparison.OrdinalIgnoreCase))
            {
                return cur;
            }
            prev = cur;
        }
        return prev;
    }

    /// <summary>桌面(资源管理器)进程名：关闭应用后前台异常时，下区回落到此。</summary>
    public const string DesktopProcessName = "explorer";

    // 关闭/切换应用后，前台可能短暂落空，或被系统服务 broker 窗口占据
    // （如 OmApSvcBroker——svchost 托管的服务弹出的 WPF 辅助窗口，实测会稳定占据前台）。
    // 这些都不是用户真正交互的程序，下区不应显示它们的空专用区，而应回落到桌面。
    private static readonly HashSet<string> ServiceBrokerProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "OmApSvcBroker" };

    /// <summary>
    /// 把前台进程名归一为“下区上下文进程”：空 或 系统服务 broker 一律回落到桌面(资源管理器)。
    /// 普通应用进程原样返回，不受影响。
    /// </summary>
    public static string ResolveContextProcess(string? process)
    {
        if (string.IsNullOrEmpty(process) || ServiceBrokerProcesses.Contains(process))
            return DesktopProcessName;
        return process;
    }

    /// <summary>
    /// 查找匹配场景。优先级：Custom > Application > Global。
    /// pageSelector 用于过滤：只匹配配置了对应页面的场景，
    /// 未配置页面的场景不参与匹配（继续向下回落）。
    /// </summary>
    public SceneMatch Match(string process, string title, string className, Func<Scene, Guid?>? pageSelector = null)
    {
        bool HasPage(Scene s) => pageSelector is null || pageSelector(s) is not null;

        // 先找自定义场景（匹配条件最多）
        var custom = _scenes
            .Where(s => s.Type == SceneType.Custom && HasPage(s) && Matches(s, process, title, className))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
        if (custom is not null)
            return new SceneMatch(custom, true);

        // 应用程序场景（进程名匹配）
        var app = _scenes
            .Where(s => s.Type == SceneType.Application && HasPage(s) && Matches(s, process, title, className))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
        if (app is not null)
            return new SceneMatch(app, true);

        // 回落全局
        var global = _scenes.FirstOrDefault(s => s.Type == SceneType.Global && HasPage(s));
        return global is not null ? new SceneMatch(global, false) : SceneMatch.None;
    }

    public SceneMatch MatchForeground(Func<Scene, Guid?>? pageSelector = null)
        => MatchForegroundCore(GetForegroundInfo(), pageSelector);

    /// <summary>
    /// 匹配全局场景（面板上区：无论哪个程序打开都显示）。
    /// </summary>
    public SceneMatch MatchGlobal(Func<Scene, Guid?>? pageSelector = null)
    {
        var global = _scenes.FirstOrDefault(s =>
            s.Type == SceneType.Global && (pageSelector is null || pageSelector(s) is not null));
        return global is not null ? new SceneMatch(global, false) : SceneMatch.None;
    }

    /// <summary>
    /// 匹配上下文场景（面板下区：只在对应应用内出现）。
    /// 优先级 Custom > Application；匹配不到返回 None（下区隐藏），不回落全局。
    /// </summary>
    public SceneMatch MatchContext(
        string process, string title, string className,
        Func<Scene, Guid?>? pageSelector = null)
    {
        bool HasPage(Scene s) => pageSelector is null || pageSelector(s) is not null;

        var custom = _scenes
            .Where(s => s.Type == SceneType.Custom && IsContextCandidate(s) && HasPage(s) && Matches(s, process, title, className))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
        if (custom is not null)
            return new SceneMatch(custom, true);

        var app = _scenes
            .Where(s => s.Type == SceneType.Application && IsContextCandidate(s) && HasPage(s) && Matches(s, process, title, className))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
        if (app is not null)
            return new SceneMatch(app, true);

        return SceneMatch.None;
    }

    /// <summary>匹配前台窗口的上下文场景（下区）</summary>
    public SceneMatch MatchContextForeground(Func<Scene, Guid?>? pageSelector = null)
    {
        var info = GetForegroundInfo();
        return MatchContext(info.process, info.title, info.className, pageSelector);
    }

    private SceneMatch MatchForegroundCore((string process, string title, string className) info, Func<Scene, Guid?>? pageSelector)
        => Match(info.process, info.title, info.className, pageSelector);

    private static bool Matches(Scene scene, string process, string title, string className)
    {
        if (!string.IsNullOrEmpty(scene.ProcessMatch)
            && !Wildcard(process, scene.ProcessMatch))
            return false;
        if (!string.IsNullOrEmpty(scene.WindowClassMatch)
            && !Wildcard(className, scene.WindowClassMatch))
            return false;
        if (!string.IsNullOrEmpty(scene.WindowTitleMatch)
            && !Wildcard(title, scene.WindowTitleMatch))
            return false;

        // 黑名单
        if (scene.Blacklist.Any(b => Wildcard(process, b)))
            return false;

        // 全屏禁用
        if (scene.DisableInFullscreen && IsForegroundFullscreen())
            return false;

        return true;
    }

    private static int Specificity(Scene scene)
    {
        var score = 0;
        if (!string.IsNullOrEmpty(scene.ProcessMatch)) score++;
        if (!string.IsNullOrEmpty(scene.WindowClassMatch)) score++;
        if (!string.IsNullOrEmpty(scene.WindowTitleMatch)) score++;
        return score;
    }

    /// <summary>
    /// 是否可作为“专用上下文场景”参与下区匹配。
    /// 专用场景必须至少有一个“有效”匹配条件（既非空、也不是纯通配 *）；
    /// 否则该场景会匹配所有程序，导致其磁贴漏到所有程序的下区，破坏
    /// “专用磁贴只在该程序出现”的隔离性。（上区全局场景不受此限制。）
    /// </summary>
    private static bool IsContextCandidate(Scene scene)
    {
        static bool Real(string v) => !string.IsNullOrWhiteSpace(v) && v.Trim() != "*";
        return Real(scene.ProcessMatch)
            || Real(scene.WindowClassMatch)
            || Real(scene.WindowTitleMatch);
    }

    public static bool Wildcard(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (pattern == "*") return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input ?? "", regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static bool IsForegroundFullscreen()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == GetDesktopWindow()) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var mon = MonitorFromWindow(h, 2); // MONITOR_DEFAULTTONEAREST
            if (mon == IntPtr.Zero || !GetMonitorInfoW(mon, ref mi)) return false;
            var sb = new StringBuilder(64);
            GetClassNameW(h, sb, sb.Capacity);
            // 排除桌面与任务栏
            if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
            return false; // 简化：暂不精确判定全屏
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 场景匹配结果
/// </summary>
public readonly struct SceneMatch
{
    public static readonly SceneMatch None = default;

    public SceneMatch(Scene scene, bool isContextScene)
    {
        Scene = scene;
        IsContextScene = isContextScene;
    }

    public Scene? Scene { get; }
    public bool IsContextScene { get; }
    public bool Found => Scene is not null;
}
