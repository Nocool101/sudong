using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Scripting;

/// <summary>
/// 脚本引擎宿主：管理各语言引擎实例。
/// </summary>
public static class ScriptEngineHost
{
    private static readonly Dictionary<string, IScriptEngine> Engines = new(StringComparer.OrdinalIgnoreCase);

    static ScriptEngineHost()
    {
        Engines["csharp"] = new CSharpScriptEngine();
        Engines["cs"] = new CSharpScriptEngine();
        Engines["javascript"] = new JavaScriptScriptEngine();
        Engines["js"] = new JavaScriptScriptEngine();
    }

    public static IScriptEngine? Get(string language)
        => Engines.TryGetValue(language, out var e) ? e : null;

    public static IEnumerable<string> SupportedLanguages => Engines.Keys;
}

/// <summary>
/// 脚本步骤集合：运行 C#/JS/Python 代码、运行脚本文件。
/// </summary>
public static class ScriptSteps
{
    public sealed class RunCSharp : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunCSharp",
            DisplayName = "运行 C# 代码",
            Category = "程序流",
            Description = "可用 ctx.Get/Set/Log 访问变量；return 返回值",
            Params =
            {
                new StepParamDef
                {
                    Name = "code", DisplayName = "C# 代码", Required = true, Multiline = true,
                    DefaultValue = "ctx.Log(\"hello\");\nreturn 1 + 1;"
                },
                new StepParamDef { Name = "resultVar", DisplayName = "结果变量名" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var code = ctx.Interpolate(step.Inputs.GetValueOrDefault("code"));
            if (string.IsNullOrWhiteSpace(code)) return StepResult.Fail("代码为空");

            var engine = ScriptEngineHost.Get("csharp");
            if (engine is null) return StepResult.Fail("C# 引擎不可用");

            var scriptCtx = new ScriptContext(ctx);
            var outcome = engine.Execute(code, scriptCtx);
            if (!outcome.Success)
                return StepResult.Fail(outcome.Error ?? "脚本执行失败");

            var resultVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("resultVar"));
            if (!string.IsNullOrEmpty(resultVar))
                ctx.Set(resultVar, outcome.Value);
            return StepResult.Ok();
        }
    }

    public sealed class RunJavaScript : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunJavaScript",
            DisplayName = "运行 JS 代码",
            Category = "程序流",
            Description = "可用 ctx.get/set/log 访问变量",
            Params =
            {
                new StepParamDef
                {
                    Name = "code", DisplayName = "JS 代码", Required = true, Multiline = true,
                    DefaultValue = "ctx.log('hello from js');\n1 + 1;"
                },
                new StepParamDef { Name = "resultVar", DisplayName = "结果变量名" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var code = ctx.Interpolate(step.Inputs.GetValueOrDefault("code"));
            if (string.IsNullOrWhiteSpace(code)) return StepResult.Fail("代码为空");

            var engine = ScriptEngineHost.Get("js");
            if (engine is null) return StepResult.Fail("JS 引擎不可用");

            var scriptCtx = new ScriptContext(ctx);
            var outcome = engine.Execute(code, scriptCtx);
            if (!outcome.Success)
                return StepResult.Fail(outcome.Error ?? "脚本执行失败");

            var resultVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("resultVar"));
            if (!string.IsNullOrEmpty(resultVar))
                ctx.Set(resultVar, outcome.Value);
            return StepResult.Ok();
        }
    }

    public sealed class RunPython : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunPython",
            DisplayName = "运行 Python 代码",
            Category = "程序流",
            Description = "通过 python.exe 执行（需安装 Python）",
            Params =
            {
                new StepParamDef { Name = "code", DisplayName = "Python 代码", Required = true, Multiline = true },
                new StepParamDef { Name = "resultVar", DisplayName = "结果变量名" },
                new StepParamDef { Name = "pythonPath", DisplayName = "python.exe 路径", DefaultValue = "python" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var code = ctx.Interpolate(step.Inputs.GetValueOrDefault("code"));
            if (string.IsNullOrWhiteSpace(code)) return StepResult.Fail("代码为空");
            var python = ctx.Interpolate(step.Inputs.GetValueOrDefault("pythonPath"));
            if (string.IsNullOrEmpty(python)) python = "python";

            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"ql_script_{Guid.NewGuid():N}.py");
                File.WriteAllText(tempFile, code, Encoding.UTF8);
                try
                {
                    var psi = new ProcessStartInfo(python, tempFile)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    using var p = Process.Start(psi);
                    if (p is null) return StepResult.Fail("Python 启动失败");
                    var output = p.StandardOutput.ReadToEnd();
                    var error = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode != 0)
                        return StepResult.Fail($"Python 退出码 {p.ExitCode}: {error}");

                    var resultVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("resultVar"));
                    if (!string.IsNullOrEmpty(resultVar))
                        ctx.Set(resultVar, output.TrimEnd());
                    return StepResult.Ok();
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"Python 执行失败: {ex.Message}");
            }
        }
    }

    public sealed class RunScriptFile : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunScriptFile",
            DisplayName = "运行脚本文件",
            Category = "程序流",
            Description = "按扩展名选择解释器：.csx/.js/.py/.ps1/.bat/.ahk",
            Params =
            {
                new StepParamDef { Name = "path", DisplayName = "脚本路径", Required = true },
                new StepParamDef { Name = "args", DisplayName = "参数" },
                new StepParamDef { Name = "resultVar", DisplayName = "结果变量名" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var path = ctx.Interpolate(step.Inputs.GetValueOrDefault("path"));
            var args = ctx.Interpolate(step.Inputs.GetValueOrDefault("args"));
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return StepResult.Fail($"脚本文件不存在: {path}");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"读取脚本失败: {ex.Message}");
            }

            var scriptCtx = new ScriptContext(ctx);
            ScriptOutcome outcome;

            switch (ext)
            {
                case ".csx":
                case ".cs":
                    var csEngine = ScriptEngineHost.Get("csharp");
                    if (csEngine is null) return StepResult.Fail("C# 引擎不可用");
                    outcome = csEngine.Execute(content, scriptCtx);
                    break;
                case ".js":
                    var jsEngine = ScriptEngineHost.Get("js");
                    if (jsEngine is null) return StepResult.Fail("JS 引擎不可用");
                    outcome = jsEngine.Execute(content, scriptCtx);
                    break;
                default:
                    // 其他类型交给系统执行
                    try
                    {
                        var psi = new ProcessStartInfo(path, args) { UseShellExecute = true };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(60000);
                        return StepResult.Ok();
                    }
                    catch (Exception ex)
                    {
                        return StepResult.Fail($"执行失败: {ex.Message}");
                    }
            }

            if (!outcome.Success) return StepResult.Fail(outcome.Error ?? "脚本失败");
            var resultVar = ctx.Interpolate(step.Inputs.GetValueOrDefault("resultVar"));
            if (!string.IsNullOrEmpty(resultVar))
                ctx.Set(resultVar, outcome.Value);
            return StepResult.Ok();
        }
    }

    /// <summary>
    /// 执行脚本（统一入口）：按 language 参数分发到具体脚本步骤。
    /// 简单动作（SimpleType=RunScript）与组合动作步骤共用此类型。
    /// </summary>
    public sealed class RunScript : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "RunScript",
            DisplayName = "执行脚本",
            Category = "程序流",
            Description = "运行 C#/JavaScript/Python 代码，或按路径运行脚本文件",
            Params =
            {
                new StepParamDef
                {
                    Name = "language", DisplayName = "语言",
                    Options = new[] { "csharp", "js", "python", "file" }, DefaultValue = "csharp"
                },
                new StepParamDef { Name = "code", DisplayName = "代码", Multiline = true },
                new StepParamDef { Name = "path", DisplayName = "脚本路径（language=file）" },
                new StepParamDef { Name = "args", DisplayName = "参数" },
                new StepParamDef { Name = "resultVar", DisplayName = "结果变量名" },
                new StepParamDef { Name = "pythonPath", DisplayName = "python.exe 路径", DefaultValue = "python" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var lang = (step.Inputs.GetValueOrDefault("language") ?? "csharp").ToLowerInvariant();
            IStep impl = lang switch
            {
                "js" or "javascript" => new RunJavaScript(),
                "python" => new RunPython(),
                "file" or "scriptfile" => new RunScriptFile(),
                _ => new RunCSharp()
            };
            return impl.Execute(ctx, step);
        }
    }
}
