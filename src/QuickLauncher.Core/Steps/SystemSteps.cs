using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// 系统步骤：日期时间、播放声音、CMD、文件读写、Everything 搜索
/// </summary>
public static class SystemSteps
{
    public sealed class GetDateTime : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "GetDateTime",
            DisplayName = "获取日期时间",
            Category = "系统",
            Params =
            {
                new StepParamDef { Name = "format", DisplayName = "格式", DefaultValue = "yyyy-MM-dd HH:mm:ss" },
                new StepParamDef { Name = "var", DisplayName = "变量名", DefaultValue = "now" },
                new StepParamDef { Name = "utc", DisplayName = "使用UTC", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var format = ctx.Interpolate(step.Inputs.GetValueOrDefault("format"));
            if (string.IsNullOrEmpty(format)) format = "yyyy-MM-dd HH:mm:ss";
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "now";
            var utc = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("utc")), out var u) && u;
            var dt = utc ? DateTime.UtcNow : DateTime.Now;
            ctx.Set(var, dt.ToString(format));
            return StepResult.Ok();
        }
    }

    public sealed class PlaySound : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "PlaySound",
            DisplayName = "播放声音",
            Category = "系统",
            Params =
            {
                new StepParamDef
                {
                    Name = "sound", DisplayName = "声音",
                    Options = new[] { "info", "succeed", "warning", "wrong", "snip" }
                },
                new StepParamDef { Name = "file", DisplayName = "或音频文件路径" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var file = ctx.Interpolate(step.Inputs.GetValueOrDefault("file"));
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    new SoundPlayer(file).Play();
                    return StepResult.Ok();
                }
                var sound = ctx.Interpolate(step.Inputs.GetValueOrDefault("sound"));
                SystemSounds.Asterisk.Play();
                _ = sound;
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"播放声音失败: {ex.Message}");
            }
        }
    }

    public sealed class RunCmd : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunCmd",
            DisplayName = "运行 CMD/PowerShell",
            Category = "系统",
            Params =
            {
                new StepParamDef
                {
                    Name = "shell", DisplayName = "Shell",
                    Options = new[] { "cmd", "powershell" }, DefaultValue = "cmd"
                },
                new StepParamDef { Name = "command", DisplayName = "命令", Required = true, Multiline = true },
                new StepParamDef { Name = "outputVar", DisplayName = "输出变量名" },
                new StepParamDef { Name = "admin", DisplayName = "管理员运行", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var shell = ctx.Interpolate(step.Inputs.GetValueOrDefault("shell"));
            var command = ctx.Interpolate(step.Inputs.GetValueOrDefault("command"));
            if (string.IsNullOrWhiteSpace(command)) return StepResult.Fail("命令为空");
            var outputVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("outputVar"));
            var admin = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("admin")), out var a) && a;

            try
            {
                var fileName = shell == "powershell" ? "powershell.exe" : "cmd.exe";
                var args = shell == "powershell" ? $"-NoProfile -Command \"{command}\"" : $"/c \"{command}\"";
                var psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = true,
                    RedirectStandardOutput = !admin && !string.IsNullOrEmpty(outputVar),
                    CreateNoWindow = !admin
                };
                if (admin) psi.Verb = "runas";

                using var p = Process.Start(psi);
                if (p is null) return StepResult.Fail("启动失败");
                if (!admin && !string.IsNullOrEmpty(outputVar))
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    ctx.Set(outputVar, output);
                }
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"运行命令失败: {ex.Message}");
            }
        }
    }

    public sealed class ReadTextFile : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "ReadTextFile",
            DisplayName = "读取文本文件",
            Category = "文件",
            Params =
            {
                new StepParamDef { Name = "path", DisplayName = "文件路径", Required = true },
                new StepParamDef { Name = "var", DisplayName = "变量名", DefaultValue = "fileContent" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var path = ctx.Interpolate(step.Inputs.GetValueOrDefault("path"));
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "fileContent";
            try
            {
                ctx.Set(var, File.ReadAllText(path));
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"读取文件失败: {ex.Message}");
            }
        }
    }

    public sealed class WriteTextFile : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "WriteTextFile",
            DisplayName = "写入文本文件",
            Category = "文件",
            Params =
            {
                new StepParamDef { Name = "path", DisplayName = "文件路径", Required = true },
                new StepParamDef { Name = "content", DisplayName = "内容", Required = true, Multiline = true },
                new StepParamDef { Name = "append", DisplayName = "追加模式", Type = VariableType.Bool }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var path = ctx.Interpolate(step.Inputs.GetValueOrDefault("path"));
            var content = ctx.Interpolate(step.Inputs.GetValueOrDefault("content")) ?? "";
            var append = bool.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("append")), out var ap) && ap;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (append) File.AppendAllText(path, content);
                else File.WriteAllText(path, content);
                return StepResult.Ok();
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"写入文件失败: {ex.Message}");
            }
        }
    }

    public sealed class WebSearch : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "WebSearch",
            DisplayName = "联网搜索",
            Category = "网络",
            Params =
            {
                new StepParamDef { Name = "engine", DisplayName = "搜索引擎", DefaultValue = "百度" },
                new StepParamDef { Name = "keyword", DisplayName = "关键词", Required = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var keyword = ctx.Interpolate(step.Inputs.GetValueOrDefault("keyword"));
            if (string.IsNullOrWhiteSpace(keyword)) return StepResult.Fail("关键词为空");
            var engine = ctx.Interpolate(step.Inputs.GetValueOrDefault("engine"));
            var url = (engine?.ToLowerInvariant()) switch
            {
                "谷歌" or "google" => $"https://www.google.com/search?q={Uri.EscapeDataString(keyword)}",
                "必应" or "bing" => $"https://cn.bing.com/search?q={Uri.EscapeDataString(keyword)}",
                _ => $"https://www.baidu.com/s?wd={Uri.EscapeDataString(keyword)}"
            };
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return StepResult.Ok();
        }
    }
}
