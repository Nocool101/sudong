using System;
using System.Collections.Generic;
using System.Linq;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// 流程控制步骤：如果/否则、每个、重复、中断、停止、等待、注释、步骤组
/// </summary>
public static class FlowSteps
{
    public sealed class If : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "If",
            DisplayName = "如果",
            Category = "程序流",
            Description = "条件为真时执行子步骤，否则执行【否则】分支",
            IsContainer = true,
            Params =
            {
                new StepParamDef
                {
                    Name = "condition", DisplayName = "条件表达式", Required = true,
                    Description = "如：{开关} == true、{数量} > 10"
                }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var condText = ctx.Interpolate(step.Inputs.GetValueOrDefault("condition"));
            var ok = ExpressionEvaluator.EvalBool(condText);
            if (ok) return StepResult.Ok();
            return StepResult.Ok(StepFlow.Continue); // 条件为假：跳过整个容器（由执行器处理 else）
        }
    }

    public sealed class Else : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Else",
            DisplayName = "否则",
            Category = "程序流",
            IsContainer = true
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
            => StepResult.Ok();
    }

    public sealed class Each : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Each",
            DisplayName = "每个",
            Category = "程序流",
            Description = "遍历列表，逐项执行子步骤",
            IsContainer = true,
            Params =
            {
                new StepParamDef { Name = "list", DisplayName = "列表变量/文本", Required = true },
                new StepParamDef { Name = "itemVar", DisplayName = "当前项变量名", DefaultValue = "item" },
                new StepParamDef { Name = "indexVar", DisplayName = "索引变量名", DefaultValue = "index" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var listRaw = ctx.ResolveParameter(step.Inputs.GetValueOrDefault("list"));
            var itemVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("itemVar"));
            if (string.IsNullOrEmpty(itemVar)) itemVar = "item";
            var indexVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("indexVar"));
            if (string.IsNullOrEmpty(indexVar)) indexVar = "index";

            List<object?> items = listRaw switch
            {
                System.Collections.IEnumerable en and not string => en.Cast<object?>().ToList(),
                string s => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Cast<object?>().ToList(),
                _ => new List<object?> { listRaw }
            };

            for (int i = 0; i < items.Count; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                ctx.Set(itemVar, items[i]);
                ctx.Set(indexVar, i);
                var flow = ctx.RunChildren(step);
                if (flow == StepFlow.Break) break;
                if (flow == StepFlow.Return || flow == StepFlow.Stop) return StepResult.Ok(flow);
            }
            return StepResult.Ok();
        }
    }

    public sealed class Repeat : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Repeat",
            DisplayName = "重复",
            Category = "程序流",
            IsContainer = true,
            Params =
            {
                new StepParamDef { Name = "count", DisplayName = "次数", Type = VariableType.Number, Required = true },
                new StepParamDef { Name = "indexVar", DisplayName = "索引变量名", DefaultValue = "index" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            if (!int.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("count")), out var count) || count < 0)
                return StepResult.Fail("重复次数无效");
            var indexVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("indexVar"));
            if (string.IsNullOrEmpty(indexVar)) indexVar = "index";

            for (int i = 0; i < count; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                ctx.Set(indexVar, i);
                var flow = ctx.RunChildren(step);
                if (flow == StepFlow.Break) break;
                if (flow == StepFlow.Return || flow == StepFlow.Stop) return StepResult.Ok(flow);
            }
            return StepResult.Ok();
        }
    }

    public sealed class Break : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Break", DisplayName = "跳出循环", Category = "程序流"
        };
        public StepResult Execute(StepContext ctx, ActionStep step) => StepResult.Ok(StepFlow.Break);
    }

    public sealed class Continue : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Continue", DisplayName = "继续下一次", Category = "程序流"
        };
        public StepResult Execute(StepContext ctx, ActionStep step) => StepResult.Ok(StepFlow.ContinueLoop);
    }

    public sealed class Stop : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Stop", DisplayName = "停止动作", Category = "程序流",
            Description = "结束当前动作（return）"
        };
        public StepResult Execute(StepContext ctx, ActionStep step) => StepResult.Ok(StepFlow.Return);
    }

    public sealed class Wait : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Wait",
            DisplayName = "等待时间",
            Category = "常用",
            Params =
            {
                new StepParamDef { Name = "ms", DisplayName = "毫秒", Type = VariableType.Number, DefaultValue = "1000" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var msText = ctx.Interpolate(step.Inputs.GetValueOrDefault("ms"));
            if (int.TryParse(msText, out var ms) && ms > 0)
                System.Threading.Thread.Sleep(ms);
            return StepResult.Ok();
        }
    }

    public sealed class Comment : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "Comment",
            DisplayName = "注释",
            Category = "常用",
            Params =
            {
                new StepParamDef { Name = "text", DisplayName = "注释内容", Multiline = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step) => StepResult.Ok();
    }

    public sealed class StepGroup : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "StepGroup",
            DisplayName = "步骤组",
            Category = "程序流",
            IsContainer = true,
            Description = "把多个步骤组织为一组"
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var flow = ctx.RunChildren(step);
            if (flow is StepFlow.Break or StepFlow.ContinueLoop) return StepResult.Ok();
            return StepResult.Ok(flow);
        }
    }
}
