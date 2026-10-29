using System;
using System.Collections.Generic;
using System.Linq;
using QuickLauncher.Models;

namespace QuickLauncher.Engine;

/// <summary>
/// 步骤类型注册表
/// </summary>
public sealed class StepRegistry
{
    private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);

    public static StepRegistry Default { get; } = CreateDefault();

    private static StepRegistry CreateDefault()
    {
        var reg = new StepRegistry();
        foreach (var step in Steps.BuiltinSteps.CreateAll())
            reg.Register(step);
        // 脚本引擎步骤（Scripting 层）：C#/JS/Python/脚本文件
        reg.Register(new Scripting.ScriptSteps.RunCSharp());
        reg.Register(new Scripting.ScriptSteps.RunJavaScript());
        reg.Register(new Scripting.ScriptSteps.RunPython());
        reg.Register(new Scripting.ScriptSteps.RunScriptFile());
        reg.Register(new Scripting.ScriptSteps.RunScript());
        return reg;
    }

    public void Register(IStep step)
    {
        _steps[step.Metadata.Type] = step;
    }

    public IStep? Get(string type)
        => _steps.TryGetValue(type, out var s) ? s : null;

    public bool Contains(string type) => _steps.ContainsKey(type);

    public IReadOnlyCollection<StepMetadata> AllMetadata
        => _steps.Values.Select(s => s.Metadata).ToList();

    public IEnumerable<StepMetadata> GetByCategory(string category)
        => _steps.Values.Where(s => s.Metadata.Category == category).Select(s => s.Metadata);

    public IEnumerable<string> Categories
        => _steps.Values.Select(s => s.Metadata.Category).Distinct().OrderBy(c => c);
}
