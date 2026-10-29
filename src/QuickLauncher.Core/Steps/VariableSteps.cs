using System;
using System.Collections.Generic;
using System.Linq;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// 变量与用户交互步骤
/// </summary>
public static class VariableSteps
{
    public sealed class SetVariable : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "SetVariable",
            DisplayName = "赋值",
            Category = "计算",
            Description = "给变量设置值（支持表达式与文本插值）",
            Params =
            {
                new StepParamDef { Name = "name", DisplayName = "变量名", Required = true },
                new StepParamDef { Name = "value", DisplayName = "值/表达式", Required = true, Multiline = true },
                new StepParamDef { Name = "persistent", DisplayName = "持久化保存", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var name = ctx.Interpolate(step.Inputs.GetValueOrDefault("name"));
            if (string.IsNullOrWhiteSpace(name)) return StepResult.Fail("变量名为空");
            var rawValue = step.Inputs.GetValueOrDefault("value") ?? "";
            object? value = rawValue.StartsWith("=")
                ? ctx.Evaluate(rawValue[1..])
                : ctx.ResolveParameter(rawValue);
            ctx.Set(name, value);

            if (bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("persistent")), out var p) && p)
                ctx.Persist(name);
            return StepResult.Ok();
        }
    }

    public sealed class ShowMessage : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "ShowMessage",
            DisplayName = "提示消息",
            Category = "界面交互",
            Params =
            {
                new StepParamDef { Name = "text", DisplayName = "内容", Required = true, Multiline = true },
                new StepParamDef
                {
                    Name = "level", DisplayName = "级别", Type = VariableType.Text,
                    Options = new[] { "info", "success", "warning", "error" }, DefaultValue = "info"
                }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var text = ctx.Interpolate(step.Inputs.GetValueOrDefault("text"));
            var level = ctx.Interpolate(step.Inputs.GetValueOrDefault("level"));
            if (ctx.Services.GetService(typeof(Services.INotificationService)) is Services.INotificationService svc)
                svc.Show(text ?? "", level ?? "info");
            else
                Console.WriteLine($"[{level}] {text}");
            return StepResult.Ok();
        }
    }

    public sealed class UserInput : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "UserInput",
            DisplayName = "用户输入",
            Category = "界面交互",
            Description = "弹出输入框，结果存入变量",
            Params =
            {
                new StepParamDef { Name = "prompt", DisplayName = "提示语", DefaultValue = "请输入" },
                new StepParamDef { Name = "default", DisplayName = "默认值" },
                new StepParamDef { Name = "var", DisplayName = "结果变量名", DefaultValue = "input" },
                new StepParamDef { Name = "multiline", DisplayName = "多行输入", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var prompt = ctx.Interpolate(step.Inputs.GetValueOrDefault("prompt"));
            var def = ctx.Interpolate(step.Inputs.GetValueOrDefault("default"));
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "input";
            var multiline = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("multiline")), out var m) && m;

            if (ctx.Services.GetService(typeof(Services.IDialogService)) is Services.IDialogService dlg)
            {
                var result = dlg.Prompt(prompt ?? "请输入", def ?? "", multiline);
                if (result is null) return StepResult.Ok(StepFlow.Return); // 取消
                ctx.Set(var, result);
            }
            return StepResult.Ok();
        }
    }

    public sealed class UserSelect : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "UserSelect",
            DisplayName = "用户选择",
            Category = "界面交互",
            Params =
            {
                new StepParamDef { Name = "prompt", DisplayName = "提示语", DefaultValue = "请选择" },
                new StepParamDef { Name = "options", DisplayName = "选项（每行一个）", Required = true, Multiline = true },
                new StepParamDef { Name = "var", DisplayName = "结果变量名", DefaultValue = "select" },
                new StepParamDef { Name = "indexVar", DisplayName = "索引变量名", DefaultValue = "selectIndex" },
                new StepParamDef { Name = "multi", DisplayName = "允许多选", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var prompt = ctx.Interpolate(step.Inputs.GetValueOrDefault("prompt"));
            var optionsText = ctx.Interpolate(step.Inputs.GetValueOrDefault("options")) ?? "";
            var options = optionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "select";
            var indexVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("indexVar"));
            if (string.IsNullOrEmpty(indexVar)) indexVar = "selectIndex";

            if (ctx.Services.GetService(typeof(Services.IDialogService)) is Services.IDialogService dlg)
            {
                var (value, index) = dlg.Select(prompt ?? "请选择", options);
                if (value is null) return StepResult.Ok(StepFlow.Return);
                ctx.Set(var, value);
                ctx.Set(indexVar, index);
            }
            return StepResult.Ok();
        }
    }

    public sealed class ListOperation : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "ListOperation",
            DisplayName = "列表操作",
            Category = "计算",
            Params =
            {
                new StepParamDef
                {
                    Name = "op", DisplayName = "操作", Required = true,
                    Options = new[] { "add", "remove", "clear", "count", "join", "sort", "reverse", "contains" }
                },
                new StepParamDef { Name = "list", DisplayName = "列表变量名", Required = true },
                new StepParamDef { Name = "item", DisplayName = "元素" },
                new StepParamDef { Name = "result", DisplayName = "结果变量名" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var op = ctx.Interpolate(step.Inputs.GetValueOrDefault("op"));
            var listName = ctx.Interpolate(step.Inputs.GetValueOrDefault("list"));
            var item = ctx.Interpolate(step.Inputs.GetValueOrDefault("item"));
            var resultVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("result"));

            if (ctx.Get<List<object?>>(listName) is not { } list)
                list = new List<object?>();

            switch (op)
            {
                case "add":
                    if (item is not null) list.Add(item);
                    ctx.Set(listName, list);
                    break;
                case "remove":
                    list.RemoveAll(x => string.Equals(x?.ToString(), item, StringComparison.Ordinal));
                    ctx.Set(listName, list);
                    break;
                case "clear":
                    list.Clear();
                    ctx.Set(listName, list);
                    break;
                case "count":
                    if (!string.IsNullOrEmpty(resultVar)) ctx.Set(resultVar, list.Count);
                    break;
                case "join":
                    var joined = string.Join("\n", list.Select(x => x?.ToString() ?? ""));
                    if (!string.IsNullOrEmpty(resultVar)) ctx.Set(resultVar, joined);
                    break;
                case "sort":
                    list.Sort((a, b) => string.CompareOrdinal(a?.ToString(), b?.ToString()));
                    ctx.Set(listName, list);
                    break;
                case "reverse":
                    list.Reverse();
                    ctx.Set(listName, list);
                    break;
                case "contains":
                    var has = list.Any(x => string.Equals(x?.ToString(), item, StringComparison.Ordinal));
                    if (!string.IsNullOrEmpty(resultVar)) ctx.Set(resultVar, has);
                    break;
            }
            return StepResult.Ok();
        }
    }
}
