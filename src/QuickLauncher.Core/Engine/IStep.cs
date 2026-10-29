using System;
using System.Collections.Generic;
using QuickLauncher.Models;

namespace QuickLauncher.Engine;

/// <summary>
/// 步骤参数定义（供编辑器 UI 使用）
/// </summary>
public sealed class StepParamDef
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public VariableType Type { get; init; } = VariableType.Text;
    public bool Required { get; init; }
    public string DefaultValue { get; init; } = "";
    /// <summary>枚举候选值（Type=Text 时作为下拉选项）</summary>
    public string[]? Options { get; init; }
    /// <summary>是否多行文本</summary>
    public bool Multiline { get; init; }
}

/// <summary>
/// 步骤元数据
/// </summary>
public sealed class StepMetadata
{
    public string Type { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "常用";
    public string Description { get; init; } = "";
    public List<StepParamDef> Params { get; init; } = new();
    /// <summary>是否容器步骤（有子步骤）</summary>
    public bool IsContainer { get; init; }
}

/// <summary>
/// 步骤接口。所有内置步骤类型实现此接口。
/// </summary>
public interface IStep
{
    StepMetadata Metadata { get; }

    StepResult Execute(StepContext context, ActionStep step);
}

/// <summary>
/// 步骤执行异常
/// </summary>
public sealed class StepException : Exception
{
    public StepException(string message) : base(message) { }
}
