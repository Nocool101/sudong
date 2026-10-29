using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace QuickLauncher.Scripting;

/// <summary>
/// C# 脚本引擎（Roslyn）。脚本中可直接使用：
/// - ctx.Get/Set/Log 访问变量与日志
/// - return 返回值
/// </summary>
public sealed class CSharpScriptEngine : IScriptEngine
{
    public string Name => "CSharp";

    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithImports("System", "System.Linq", "System.Collections.Generic", "QuickLauncher.Scripting")
        .WithReferences(
            typeof(ScriptContext).Assembly,
            typeof(object).Assembly,
            typeof(Enumerable).Assembly);

    public ScriptOutcome Execute(string code, ScriptContext context)
    {
        try
        {
            var globals = new ScriptGlobals { ctx = context };
            var task = CSharpScript.RunAsync<object?>(code, Options, globals);
            task.Wait(TimeSpan.FromSeconds(30));
            if (task.IsFaulted)
            {
                var ex = task.Exception?.InnerException ?? task.Exception;
                return ScriptOutcome.Fail(ex?.Message ?? "脚本异常");
            }
            return ScriptOutcome.Ok(task.Result.ReturnValue, context.ScriptLog);
        }
        catch (Exception ex)
        {
            return ScriptOutcome.Fail(ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>Roslyn 脚本的全局对象（小写字段名便于脚本直接使用）</summary>
    public sealed class ScriptGlobals
    {
#pragma warning disable IDE1006, SA1309 // 字段名故意用小写供脚本直接访问
        public ScriptContext? ctx;
#pragma warning restore IDE1006, SA1309
    }
}
