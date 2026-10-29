using System;
using System.Collections.Generic;

namespace QuickLauncher.Models;

/// <summary>
/// 全局设置
/// </summary>
public sealed class AppSettings
{
    /// <summary>面板触发方式</summary>
    public PanelTrigger PanelTrigger { get; set; } = new();

    /// <summary>外观设置</summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>常规设置</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>文本指令触发字符（默认 Tab）</summary>
    public string TextCommandTriggerChar { get; set; } = "Tab";

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 面板触发配置
/// </summary>
public sealed class PanelTrigger
{
    /// <summary>单击中键弹出</summary>
    public bool MiddleClick { get; set; } = true;

    /// <summary>快击 Ctrl 弹出（长按超过 FastClickMs 视为普通键）</summary>
    public bool CtrlClick { get; set; } = true;

    /// <summary>快击判定毫秒数</summary>
    public int FastClickMs { get; set; } = 200;

    /// <summary>面板跟随鼠标</summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>自动激活鼠标位置窗口</summary>
    public bool AutoActivateWindow { get; set; }

    /// <summary>在按钮上松开即执行</summary>
    public bool ExecuteOnRelease { get; set; }

    /// <summary>
    /// 触发面板的鼠标按钮集合（可多选）。可选值：Middle / X1 / X2 / Right / Left。
    /// 空列表时回落到 MiddleClick 开关（兼容旧数据）。
    /// </summary>
    public List<string> TriggerButtons { get; set; } = new();

    /// <summary>生效的触发按钮集合（兼容旧数据）</summary>
    public IEnumerable<string> EffectiveButtons
    {
        get
        {
            if (TriggerButtons is { Count: > 0 }) return TriggerButtons;
            var list = new List<string>();
            if (MiddleClick) list.Add("Middle");
            return list;
        }
    }
}

/// <summary>
/// 外观设置
/// </summary>
public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "Dark";

    public string AccentColor { get; set; } = "#FF0078D7";

    public int PanelButtonSize { get; set; } = 48;

    public double Opacity { get; set; } = 0.95;

    public bool ShowButtonLabels { get; set; } = true;

    public bool PlaySounds { get; set; } = true;
}

/// <summary>
/// 常规设置
/// </summary>
public sealed class GeneralSettings
{
    /// <summary>开机自启</summary>
    public bool RunAtStartup { get; set; } = true;

    /// <summary>启用动作使用统计</summary>
    public bool TrackUsage { get; set; } = true;

    /// <summary>全局禁用快捷键</summary>
    public string SuspendHotkey { get; set; } = "Ctrl+Alt+Q";

    /// <summary>搜索引擎列表（名称 -> URL 模板，%s 为占位）</summary>
    public List<SearchEngine> SearchEngines { get; set; } = new();

    /// <summary>显示桌面悬浮按钮</summary>
    public bool ShowFloatingButton { get; set; }

    /// <summary>
    /// 写入调试日志到 %TEMP%\ql_debug.log（仅本机、无任何网络上传）。默认关闭；
    /// 即便开启也不记录窗口标题，仅记录进程名/类名等定位信息，并限制 1MB 轮转。
    /// </summary>
    public bool DebugLog { get; set; }
}

/// <summary>
/// 搜索引擎
/// </summary>
public sealed class SearchEngine
{
    public string Name { get; set; } = "";

    public string UrlTemplate { get; set; } = "";
}

/// <summary>
/// 文本指令：缩写词 + 触发字符
/// </summary>
public sealed class TextCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>缩写词（可直接键入的字符）</summary>
    public string Abbreviation { get; set; } = "";

    /// <summary>执行的动作 Id</summary>
    public Guid ActionId { get; set; }

    /// <summary>单独触发字符（空 = 用全局默认）</summary>
    public string TriggerChar { get; set; } = "";

    /// <summary>是否正则匹配</summary>
    public bool IsRegex { get; set; }

    /// <summary>绑定的进程（空 = 全局）</summary>
    public List<string> Processes { get; set; } = new();

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 扩展热键：引导键 + 动作键
/// </summary>
public sealed class HotkeyCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>引导键（如 CapsLock）</summary>
    public string LeaderKey { get; set; } = "";

    /// <summary>动作键（空 = 单独按引导键触发）</summary>
    public string ActionKey { get; set; } = "";

    /// <summary>修饰键（Ctrl/Alt/Shift/Win 组合）</summary>
    public string Modifiers { get; set; } = "";

    /// <summary>执行的动作 Id</summary>
    public Guid ActionId { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 高级鼠标触发规则（规则引擎按序匹配）
/// </summary>
public sealed class MouseTriggerRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>鼠标动作：MiddleClick / RightHold+Move / Circle 等</summary>
    public string MouseAction { get; set; } = "";

    /// <summary>屏幕位置条件（空 = 任意）</summary>
    public string ScreenZone { get; set; } = "";

    /// <summary>修饰键</summary>
    public string Modifiers { get; set; } = "";

    /// <summary>生效进程白名单（空 = 全部）</summary>
    public List<string> ProcessWhitelist { get; set; } = new();

    /// <summary>生效进程黑名单</summary>
    public List<string> ProcessBlacklist { get; set; } = new();

    /// <summary>加载的动作页 Id</summary>
    public Guid? PageId { get; set; }

    /// <summary>或直接执行的动作 Id</summary>
    public Guid? ActionId { get; set; }

    public int Order { get; set; }

    public bool Enabled { get; set; } = true;
}
