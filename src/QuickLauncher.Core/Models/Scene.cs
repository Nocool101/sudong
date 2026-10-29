using System;
using System.Collections.Generic;

namespace QuickLauncher.Models;

/// <summary>
/// 场景类型：全局 / 应用程序 / 自定义
/// </summary>
public enum SceneType
{
    Global,
    Application,
    Custom
}

/// <summary>
/// 场景。每个场景独立配置面板动作页、匹配规则、黑名单等。
/// </summary>
public sealed class Scene
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新场景";

    public SceneType Type { get; set; } = SceneType.Application;

    public bool Enabled { get; set; } = true;

    /// <summary>进程名匹配（支持 * 通配，如 chrome*）</summary>
    public string ProcessMatch { get; set; } = "";

    /// <summary>窗口类名匹配（可空）</summary>
    public string WindowClassMatch { get; set; } = "";

    /// <summary>窗口标题匹配（可空，支持通配）</summary>
    public string WindowTitleMatch { get; set; } = "";

    /// <summary>面板加载的动作页</summary>
    public Guid? PanelPageId { get; set; }

    /// <summary>轮盘加载的动作页（轮盘功能已移除，保留字段兼容旧数据）</summary>
    public Guid? WheelPageId { get; set; }

    /// <summary>黑名单进程（在这些进程中禁用本场景）</summary>
    public List<string> Blacklist { get; set; } = new();

    /// <summary>全屏时禁用</summary>
    public bool DisableInFullscreen { get; set; }
}

/// <summary>
/// 动作页（面板的内容组织）
/// </summary>
public sealed class ActionPage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新动作页";

    /// <summary>面板网格列数</summary>
    public int Columns { get; set; } = 4;

    /// <summary>面板网格行数（0 = 自适应）</summary>
    public int Rows { get; set; }

    /// <summary>单元格：位置 -> 动作 Id（空 = 空格子）</summary>
    public List<ActionCell> Cells { get; set; } = new();

    /// <summary>是否为轮盘页</summary>
    public bool IsWheel { get; set; }

    /// <summary>轮盘位数（内圈）</summary>
    public int WheelSlots { get; set; } = 8;

    /// <summary>轮盘第二圈（外圈）槽位</summary>
    public List<ActionCell> OuterCells { get; set; } = new();

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 动作页中的一个格子
/// </summary>
public sealed class ActionCell
{
    /// <summary>索引位置（0 起）</summary>
    public int Index { get; set; }

    public Guid ActionId { get; set; }

    /// <summary>格子内覆盖的显示名称（空 = 用动作名）</summary>
    public string OverrideName { get; set; } = "";

    /// <summary>格子内覆盖的图标（空 = 用动作图标）</summary>
    public string OverrideIcon { get; set; } = "";
}
