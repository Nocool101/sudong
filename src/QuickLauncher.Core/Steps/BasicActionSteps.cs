using System;
using System.Diagnostics;
using System.IO;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Services;

namespace QuickLauncher.Steps;

/// <summary>
/// 基础动作步骤：打开网址/文件/文件夹、运行程序、发送文本、模拟按键
/// </summary>
public static class BasicActionSteps
{
    public sealed class OpenUrl : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "OpenUrl",
            DisplayName = "打开网址",
            Category = "常用",
            Description = "用默认浏览器打开网址",
            Params =
            {
                new StepParamDef { Name = "url", DisplayName = "网址", Required = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var url = ctx.Interpolate(step.Inputs.GetValueOrDefault("url"));
            if (string.IsNullOrWhiteSpace(url)) return StepResult.Fail("网址为空");
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"打开网址失败: {ex.Message}");
            }
        }
    }

    public sealed class OpenFile : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "OpenFile",
            DisplayName = "打开文件",
            Category = "常用",
            Params =
            {
                new StepParamDef { Name = "path", DisplayName = "文件路径", Required = true },
                new StepParamDef { Name = "args", DisplayName = "参数" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var path = ctx.Interpolate(step.Inputs.GetValueOrDefault("path"));
            var args = ctx.Interpolate(step.Inputs.GetValueOrDefault("args"));
            if (string.IsNullOrWhiteSpace(path)) return StepResult.Fail("文件路径为空");
            try
            {
                Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"打开文件失败: {ex.Message}");
            }
        }
    }

    public sealed class OpenFolder : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "OpenFolder",
            DisplayName = "打开文件夹",
            Category = "常用",
            Params =
            {
                new StepParamDef { Name = "path", DisplayName = "文件夹路径", Required = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var path = ctx.Interpolate(step.Inputs.GetValueOrDefault("path"));
            if (string.IsNullOrWhiteSpace(path)) return StepResult.Fail("文件夹路径为空");
            try
            {
                if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
                else if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                    return StepResult.Fail($"路径不存在: {path}");
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"打开文件夹失败: {ex.Message}");
            }
        }
    }

    public sealed class RunProgram : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunProgram",
            DisplayName = "运行程序",
            Category = "常用",
            Params =
            {
                new StepParamDef { Name = "program", DisplayName = "程序/命令", Required = true },
                new StepParamDef { Name = "args", DisplayName = "参数" },
                new StepParamDef
                {
                    Name = "windowStyle", DisplayName = "窗口样式", Type = VariableType.Text,
                    Options = new[] { "normal", "hidden", "minimized", "maximized" },
                    DefaultValue = "normal"
                },
                new StepParamDef { Name = "wait", DisplayName = "等待结束", Type = VariableType.Bool },
                new StepParamDef { Name = "captureOutput", DisplayName = "捕获输出", Type = VariableType.Bool },
                new StepParamDef { Name = "outputVar", DisplayName = "输出变量名" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var program = ctx.Interpolate(step.Inputs.GetValueOrDefault("program"));
            var args = ctx.Interpolate(step.Inputs.GetValueOrDefault("args"));
            if (string.IsNullOrWhiteSpace(program)) return StepResult.Fail("程序为空");
            var wait = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("wait")), out var w) && w;
            var capture = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("captureOutput")), out var c) && c;

            try
            {
                var psi = new ProcessStartInfo(program, args) { UseShellExecute = !capture };
                var style = ctx.Interpolate(step.Inputs.GetValueOrDefault("windowStyle"));
                psi.WindowStyle = style switch
                {
                    "hidden" => ProcessWindowStyle.Hidden,
                    "minimized" => ProcessWindowStyle.Minimized,
                    "maximized" => ProcessWindowStyle.Maximized,
                    _ => ProcessWindowStyle.Normal
                };
                if (capture)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    using var p = Process.Start(psi);
                    if (p is null) return StepResult.Fail("进程启动失败");
                    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (wait) p.WaitForExit();
                    var varName = ctx.Interpolate(step.Inputs.GetValueOrDefault("outputVar"));
                    if (!string.IsNullOrEmpty(varName)) ctx.Set(varName, output);
                }
                else
                {
                    using var p = Process.Start(psi);
                    if (p is null) return StepResult.Fail("进程启动失败");
                    if (wait) p.WaitForExit();
                }
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"运行程序失败: {ex.Message}");
            }
        }
    }

    public sealed class SendText : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "SendText",
            DisplayName = "发送文本",
            Category = "常用",
            Description = "向当前焦点窗口输入文本（支持中文）",
            Params =
            {
                new StepParamDef { Name = "text", DisplayName = "文本内容", Required = true, Multiline = true },
                new StepParamDef
                {
                    Name = "mode", DisplayName = "输入方式", Type = VariableType.Text,
                    Options = new[] { "type", "paste" }, DefaultValue = "type"
                },
                new StepParamDef { Name = "delay", DisplayName = "输入前延迟(ms)", Type = VariableType.Number, DefaultValue = "0" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var text = ctx.Interpolate(step.Inputs.GetValueOrDefault("text"));
            if (text is null) return StepResult.Ok();
            var mode = ctx.Interpolate(step.Inputs.GetValueOrDefault("mode"));
            if (int.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("delay")), out var delay) && delay > 0)
                System.Threading.Thread.Sleep(delay);

            try
            {
                if (mode == "paste")
                {
                    ClipboardSteps.SetText(text);
                    KeySyntax.Send("^v");
                }
                else
                {
                    Win32.SendText(text);
                }
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"发送文本失败: {ex.Message}");
            }
        }
    }

    public sealed class SendKeys : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "SendKeys",
            DisplayName = "模拟按键",
            Category = "常用",
            Description = "Quicker 按键语法：^=Ctrl !=Alt +=Shift #=Win，{F1} 等命名键",
            Params =
            {
                new StepParamDef { Name = "keys", DisplayName = "按键", Required = true },
                new StepParamDef { Name = "delay", DisplayName = "输入前延迟(ms)", Type = VariableType.Number, DefaultValue = "0" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var keys = ctx.Interpolate(step.Inputs.GetValueOrDefault("keys"));
            if (string.IsNullOrWhiteSpace(keys)) return StepResult.Ok();
            if (int.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("delay")), out var delay) && delay > 0)
                System.Threading.Thread.Sleep(delay);
            try
            {
                KeySyntax.Send(keys);
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"模拟按键失败: {ex.Message}");
            }
        }
    }

    public sealed class SwitchPage : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "SwitchPage",
            DisplayName = "加载动作页",
            Category = "常用",
            Description = "切换面板当前显示的动作页",
            Params =
            {
                new StepParamDef { Name = "pageId", DisplayName = "动作页 Id", Required = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var pageIdText = ctx.Interpolate(step.Inputs.GetValueOrDefault("pageId"));
            if (string.IsNullOrWhiteSpace(pageIdText)) return StepResult.Fail("页面 Id 为空");
            if (!Guid.TryParse(pageIdText, out var pageId))
                return StepResult.Fail($"页面 Id 无效: {pageIdText}");
            if (ctx.Services.GetService(typeof(IPanelService)) is not IPanelService panel)
                return StepResult.Fail("面板服务不可用（仅 UI 模式支持加载动作页）");
            panel.SwitchToPage(pageId);
            return StepResult.Ok();
        }
    }

}
