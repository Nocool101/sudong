using System;
using System.Collections.Generic;
using QuickLauncher.Models;
using QuickLauncher.Storage;

namespace QuickLauncher.Engine;

/// <summary>
/// 动作执行结果
/// </summary>
public sealed class ActionRunResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> Log { get; init; } = new();
    public TimeSpan Duration { get; init; }
    public Dictionary<string, string> Outputs { get; init; } = new();
}

/// <summary>
/// 步骤解释执行器：顺序执行步骤列表，处理容器与控制流。
/// </summary>
public sealed class StepExecutor
{
    private readonly StepRegistry _registry;
    private readonly Storage.ActionStore _store;
    private readonly IServiceProvider _services;

    public StepExecutor(StepRegistry registry, Storage.ActionStore store, IServiceProvider services)
    {
        _registry = registry;
        _store = store;
        _services = services;
    }

    /// <summary>
    /// 执行一个动作
    /// </summary>
    public ActionRunResult Execute(ActionItem action, Dictionary<string, string>? inputs = null,
        System.Threading.CancellationToken ct = default)
    {
        var log = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ctx = new StepContext(action.Id, _store, _services)
        {
            CancellationToken = ct
        };
        if (inputs is not null)
            foreach (var kv in inputs)
                ctx.InputParameters[kv.Key] = kv.Value;

        try
        {
            // 初始化默认变量值
            foreach (var v in action.Variables)
            {
                if (!v.DefaultValue.StartsWith("{")) // 默认值为表达式时不预置
                    ctx.Set(v.Name, v.DefaultValue);
            }

            // 容器步骤回调
            ctx.ChildrenRunner = s => ExecuteList(s.Children, ctx, log);

            if (action.Kind == ActionKind.Simple)
            {
                ExecuteSimple(action, ctx, log);
            }
            else
            {
                var flow = ExecuteList(action.Steps, ctx, log);
                if (flow == StepFlow.Return || flow == StepFlow.Stop) { /* 正常结束 */ }
            }

            ctx.CommitPersistent();
            sw.Stop();
            return new ActionRunResult
            {
                Success = true,
                Log = log,
                Duration = sw.Elapsed,
                Outputs = new Dictionary<string, string>(ctx.OutputParameters)
            };
        }
        catch (OperationCanceledException)
        {
            return new ActionRunResult { Success = false, Error = "已取消", Log = log, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            log.Add($"异常: {ex.Message}");
            sw.Stop();
            return new ActionRunResult { Success = false, Error = ex.Message, Log = log, Duration = sw.Elapsed };
        }
    }

    /// <summary>
    /// 执行步骤列表，返回控制流
    /// </summary>
    public StepFlow ExecuteList(List<ActionStep> steps, StepContext ctx, List<string> log)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!step.Enabled) continue;

            // If/Else 配对处理
            if (step.StepType.Equals("If", StringComparison.OrdinalIgnoreCase))
            {
                var condText = ctx.Interpolate(step.Inputs.GetValueOrDefault("condition"));
                var ok = ctx.EvaluateBool(condText);
                log.Add($"[如果] {condText} => {ok}");
                if (ok)
                {
                    var f = ExecuteList(step.Children, ctx, log);
                    if (f is StepFlow.Return or StepFlow.Stop or StepFlow.Break or StepFlow.ContinueLoop)
                        return f;
                }
                else
                {
                    // 找配对的 Else
                    var elseStep = i + 1 < steps.Count && steps[i + 1].StepType.Equals("Else", StringComparison.OrdinalIgnoreCase)
                        ? steps[i + 1]
                        : null;
                    if (elseStep is not null)
                    {
                        var f = ExecuteList(elseStep.Children, ctx, log);
                        if (f is StepFlow.Return or StepFlow.Stop or StepFlow.Break or StepFlow.ContinueLoop)
                            return f;
                    }
                }
                continue;
            }
            if (step.StepType.Equals("Else", StringComparison.OrdinalIgnoreCase))
                continue; // 已被 If 消费

            // 步骤级条件
            if (!string.IsNullOrWhiteSpace(step.Condition))
            {
                if (!EvaluateCondition(step.Condition, ctx)) continue;
            }

            var impl = _registry.Get(step.StepType);
            if (impl is null)
            {
                log.Add($"[跳过] 未知步骤类型: {step.StepType}");
                continue;
            }

            log.Add($"[{impl.Metadata.DisplayName}] {step.Comment}".TrimEnd());
            var result = impl.Execute(ctx, step);
            if (!result.Success)
                throw new StepException(result.Error ?? $"步骤失败: {step.StepType}");

            switch (result.Flow)
            {
                case StepFlow.Break:
                case StepFlow.ContinueLoop:
                case StepFlow.Return:
                case StepFlow.Stop:
                    return result.Flow;
            }
        }
        return StepFlow.Continue;
    }

    private static bool EvaluateCondition(string expr, StepContext ctx)
    {
        var text = ctx.Interpolate(expr);
        if (bool.TryParse(text, out var b)) return b;
        return ExpressionEvaluator.TryEvaluate(text, out var r) && r;
    }

    /// <summary>
    /// 简单动作类型 -> 底层步骤类型（RunScript 按 language 的内部分发在步骤内完成）。
    /// </summary>
    private static string? MapSimpleTypeToStep(string simpleType)
    {
        return simpleType switch
        {
            "OpenUrl" => "OpenUrl",
            "OpenFile" => "OpenFile",
            "OpenFolder" => "OpenFolder",
            "RunProgram" => "RunProgram",
            "RunCommand" => "RunCmd",
            "SendText" => "SendText",
            "SendKeys" => "SendKeys",
            "SwitchPage" => "SwitchPage",
            "RunScript" => "RunScript",
            _ => null
        };
    }

    private void ExecuteSimple(ActionItem action, StepContext ctx, List<string> log)
    {
        var inputs = action.SimpleInputs ?? new Dictionary<string, string>();

        // 简单动作转为对应内置步骤执行
        var stepType = MapSimpleTypeToStep(action.SimpleType);
        if (stepType is null)
        {
            log.Add($"[警告] 未支持的简单动作类型: {action.SimpleType}");
            return;
        }

        var step = new ActionStep { StepType = stepType };
        // 新版：专属表单的多参数
        foreach (var kv in inputs)
            step.Inputs[kv.Key] = kv.Value;
        // 向后兼容：旧数据仅 SimpleParam 单参数，按该类型的主参数名填入
        if (inputs.Count == 0 && !string.IsNullOrEmpty(action.SimpleParam))
        {
            var mainParam = action.SimpleType switch
            {
                "OpenUrl" => "url",
                "OpenFile" or "OpenFolder" => "path",
                "RunProgram" => "program",
                "RunCommand" => "command",
                "SendText" => "text",
                "SendKeys" => "keys",
                "SwitchPage" => "pageId",
                _ => null
            };
            if (mainParam is not null) step.Inputs[mainParam] = action.SimpleParam;
        }

        var impl = _registry.Get(stepType);
        if (impl is null) { log.Add($"[警告] 步骤未注册: {stepType}"); return; }
        var r = impl.Execute(ctx, step);
        if (!r.Success) throw new StepException(r.Error ?? "简单动作失败");
        var desc = step.Inputs.Values.FirstOrDefault() ?? "";
        log.Add($"[{impl.Metadata.DisplayName}] {desc}");
    }

    private Storage.ActionStore ResolveStore()
    {
        if (_services.GetService(typeof(Storage.ActionStore)) is Storage.ActionStore store)
            return store;
        throw new InvalidOperationException("ActionStore 未注册到服务容器");
    }
}