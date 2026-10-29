using System;
using System.Collections.Generic;

namespace QuickLauncher.Models;

/// <summary>
/// 动作类型
/// </summary>
public enum ActionKind
{
    /// <summary>简单动作（单步内置类型）</summary>
    Simple,
    /// <summary>组合动作（多步骤编排）</summary>
    Composite,
    /// <summary>子程序（带输入输出参数的组合动作）</summary>
    Subprogram
}

/// <summary>
/// 动作定义。对应 Quicker 的 Action：图标 + 名称 + 步骤 + 变量 + 右键菜单。
/// </summary>
public sealed class ActionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新动作";

    /// <summary>图标描述：[fa:Solid_Play:#FF6600] / [url:http://...] / 文件路径 / 单个字符</summary>
    public string Icon { get; set; } = "";

    public string Description { get; set; } = "";

    public ActionKind Kind { get; set; } = ActionKind.Simple;

    /// <summary>简单动作的类型标识（如 OpenUrl / SendText / RunProgram / RunCommand / SwitchPage / RunScript）</summary>
    public string SimpleType { get; set; } = "";

    /// <summary>简单动作的参数（旧版单参数，保留兼容）</summary>
    public string SimpleParam { get; set; } = "";

    /// <summary>
    /// 简单动作的多参数表单（参数名 -> 值），参数定义取自对应步骤类型的 StepParamDef。
    /// 为空时执行层回退到 SimpleParam 单参数（兼容旧数据）。
    /// </summary>
    public Dictionary<string, string> SimpleInputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>组合动作的步骤列表</summary>
    public List<ActionStep> Steps { get; set; } = new();

    /// <summary>变量定义</summary>
    public List<ActionVariable> Variables { get; set; } = new();

    /// <summary>自定义右键菜单 DSL（[fa:...]文本|命令、---- 分隔、缩进子菜单）</summary>
    public string ContextMenu { get; set; } = "";

    /// <summary>快捷键（如 Ctrl+Alt+P）</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>可见性表达式（空 = 始终可见）</summary>
    public string VisibilityExpression { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>子程序参数定义（仅 Kind=Subprogram）</summary>
    public List<SubprogramParameter> Parameters { get; set; } = new();

    public ActionItem Clone()
    {
        var json = Json.Serialize(this);
        return Json.Deserialize<ActionItem>(json)!;
    }
}

/// <summary>
/// 组合动作中的一个步骤
/// </summary>
public sealed class ActionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>步骤类型标识，对应 StepRegistry 中注册的名称</summary>
    public string StepType { get; set; } = "";

    public string Comment { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>输入参数：参数名 -> 静态值或 {变量}/{表达式}</summary>
    public Dictionary<string, string> Inputs { get; set; } = new();

    /// <summary>输出参数：步骤输出名 -> 目标变量名</summary>
    public Dictionary<string, string> Outputs { get; set; } = new();

    /// <summary>步骤级条件（为空则总是执行）</summary>
    public string Condition { get; set; } = "";

    /// <summary>子步骤（容器步骤：步骤组/如果/每个/重复）</summary>
    public List<ActionStep> Children { get; set; } = new();
}

/// <summary>
/// 变量类型
/// </summary>
public enum VariableType
{
    Text,
    Number,
    Bool,
    List,
    Dict,
    Table,
    Date,
    Object
}

/// <summary>
/// 动作变量定义
/// </summary>
public sealed class ActionVariable
{
    public string Name { get; set; } = "";

    public VariableType Type { get; set; } = VariableType.Text;

    public string DefaultValue { get; set; } = "";

    /// <summary>是否为状态变量（持久化到 state_&lt;ActionId&gt;.json）</summary>
    public bool Persistent { get; set; }

    public string Description { get; set; } = "";
}

/// <summary>
/// 子程序参数
/// </summary>
public sealed class SubprogramParameter
{
    public string Name { get; set; } = "";

    public VariableType Type { get; set; } = VariableType.Text;

    public string DefaultValue { get; set; } = "";

    /// <summary>方向：Input / Output / InOut</summary>
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public string Description { get; set; } = "";
}

public enum ParameterDirection
{
    Input,
    Output,
    InOut
}
