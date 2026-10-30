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

    /// <summary>
    /// 脚本执行超时。超时后放弃等待并返回失败——Roslyn 脚本无法强制中断运行中的代码，
    /// 失控的脚本线程只能任其自生自灭（线程池线程为后台线程，不阻止进程退出）。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public ScriptOutcome Execute(string code, ScriptContext context)
    {
        try
        {
            var globals = new ScriptGlobals { ctx = context };

            // 【关键·两处坑】
            // 1) Roslyn 的 RunAsync 会【同步】执行脚本体（脚本里没有 await 时，整个 body
            //    就在调用线程上跑完）。直接调用 → 调用方被死循环脚本永久阻塞，
            //    之后再加超时也无济于事（实测 while/Sleep 60s 的脚本会真的卡住 60 秒）。
            //    所以必须把「编译 + 执行」整体放到线程池线程，调用方才可能真正 enforce 超时。
            // 2) 拿到 Task 后必须检查 Wait 的返回值：超时后继续走 task.Result 会永久阻塞。
            var task = Task.Run(() =>
                CSharpScript.RunAsync<object?>(code, Options, globals).GetAwaiter().GetResult());

            if (!task.Wait(Timeout))
            {
                // 超时：Roslyn 无法强制中断运行中的脚本，只能放弃那个失控线程
                // （线程池线程是后台线程，不阻止进程退出）。
                return ScriptOutcome.Fail(
                    $"脚本执行超时（{Timeout.TotalSeconds:0} 秒），可能存在死循环");
            }

            // Wait 在任务 faulted 时抛 AggregateException，由下面的 catch 处理
            var state = task.Result;
            return ScriptOutcome.Ok(state.ReturnValue, context.ScriptLog);
        }
        catch (AggregateException agg)
        {
            var ex = agg.InnerException ?? agg;
            return ScriptOutcome.Fail(ex.Message);
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
