using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Scripting;
using QuickLauncher.Services;
using QuickLauncher.Storage;
using Xunit;

namespace QuickLauncher.Tests;

/// <summary>
/// P0 缺陷回归测试。
///
/// 这里的每个用例都对应一个曾经「功能完全失效但没有任何测试能发现」的问题：
/// 变量只写不读、条件恒为假、自触发防护形同虚设、脚本超时挂死线程、命令步骤必崩。
/// </summary>
public class P0RegressionTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ActionStore _store;

    public P0RegressionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ql_p0_" + Guid.NewGuid().ToString("N")[..8]);
        _db = new Database(_dir);
        _store = new ActionStore(_db, _dir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubPanelService : IPanelService
    {
        public void ShowPanel(int mouseX, int mouseY) { }
        public void HidePanel() { }
        public bool IsPanelOpen => false;
        public bool IsInteracting => false;
        public (int x, int y, int w, int h)? GetPanelRect() => null;
        public event Action? PanelShown { add { } remove { } }
        public void SwitchToPage(Guid pageId) { }
        public bool IsSuspended { get; set; }
    }

    private StepContext NewContext() => new(Guid.NewGuid(), _store, new NullServiceProvider());

    private StepExecutor NewExecutor()
        => new(StepRegistry.Default, _store, new NullServiceProvider());

    // ─────────────────────────────────────────────────────────────
    // 1) 持久变量：state 文件的键带 "$var:" 前缀，运行时变量名必须【不带】前缀
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PersistentVariable_LoadedUnderUnprefixedName()
    {
        var actionId = Guid.NewGuid();
        _store.SetStateValue(actionId, "$var:counter", "42");

        // 新建上下文 = 模拟「新一次运行」读取上次的持久变量
        var ctx = new StepContext(actionId, _store, new NullServiceProvider());

        Assert.Equal("42", ctx.GetString("counter"));
        Assert.Equal("42", ctx.Interpolate("{counter}"));
        // 旧的带前缀写法不应污染运行时变量名
        Assert.Null(ctx.GetString("$var:counter"));
    }

    [Fact]
    public void PersistentVariable_SurvivesExecutorRoundTrip()
    {
        var executor = NewExecutor();
        var action = new ActionItem { Name = "持久化往返", Kind = ActionKind.Composite };
        action.Steps.Add(new ActionStep
        {
            StepType = "SetVariable",
            Inputs = { ["name"] = "counter", ["value"] = "=1 + 1", ["persistent"] = "true" }
        });

        var r1 = executor.Execute(action);
        Assert.True(r1.Success, r1.Error);
        Assert.Equal("2", _store.GetStateValue(action.Id, "$var:counter"));

        // 第二次运行：新上下文应能把 counter 读回来（修复前读回的是 "$var:counter"）
        var ctx = new StepContext(action.Id, _store, new NullServiceProvider());
        Assert.Equal("2", ctx.GetString("counter"));
    }

    // ─────────────────────────────────────────────────────────────
    // 2) 表达式：{变量} 必须是【引用】而非文本替换，否则字符串比较恒为假
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Expression_StringCompare_WithBraceVariable()
    {
        var ctx = NewContext();
        ctx.Set("状态", "已完成");
        ctx.Set("数量", 10);

        // 修复前：Interpolate 把 {状态} 换成裸文本 → "已完成 == \"已完成\"" →
        // 解析器当成标识符查找失败 → 恒 false
        Assert.True(ctx.EvaluateBool("{状态} == \"已完成\""));
        Assert.False(ctx.EvaluateBool("{状态} == \"未完成\""));

        // 数字比较（修复前后都应正常）
        Assert.True(ctx.EvaluateBool("{数量} > 5"));
        Assert.False(ctx.EvaluateBool("{数量} > 50"));

        // 裸标识符形式
        Assert.True(ctx.EvaluateBool("状态 == \"已完成\""));

        // 逻辑组合
        Assert.True(ctx.EvaluateBool("{数量} > 5 and {状态} == \"已完成\""));
    }

    [Fact]
    public void If_Step_WithStringCondition_TakesBranch()
    {
        var executor = NewExecutor();
        var action = new ActionItem { Name = "条件分支", Kind = ActionKind.Composite };
        action.Steps.Add(new ActionStep
        {
            StepType = "SetVariable",
            Inputs = { ["name"] = "状态", ["value"] = "已完成" }
        });
        action.Steps.Add(new ActionStep
        {
            StepType = "If",
            Inputs = { ["condition"] = "{状态} == \"已完成\"" },
            Children =
            {
                new ActionStep
                {
                    StepType = "SetVariable",
                    Inputs = { ["name"] = "命中", ["value"] = "yes" }
                }
            }
        });

        var result = executor.Execute(action);
        Assert.True(result.Success, result.Error);
        Assert.Contains("=> True", string.Join("\n", result.Log));
    }

    [Fact]
    public void Expression_BraceVariable_WithFormat()
    {
        var ctx = NewContext();
        ctx.Set("今天", new DateTime(2026, 10, 30));
        Assert.Equal("2026-10-30", ctx.Evaluate("{今天:yyyy-MM-dd}"));
    }

    // ─────────────────────────────────────────────────────────────
    // 3) Suppress()：抑制区必须覆盖整个动作执行期，否则合成输入会回灌自身钩子
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RunAction_SuppressionCoversExecutionWindow()
    {
        var executor = NewExecutor();
        var hooks = new QuickLauncher.App.Hooks.GlobalHooks();
        var matcher = new QuickLauncher.App.Scenes.SceneMatcher(new List<Scene>());
        var controller = new QuickLauncher.App.Interaction.InteractionController(
            hooks, matcher, _store, executor, new StubPanelService(), new AppSettings());

        var action = new ActionItem { Name = "慢动作", Kind = ActionKind.Composite };
        action.Steps.Add(new ActionStep { StepType = "Wait", Inputs = { ["ms"] = "900" } });
        _store.SaveAction(action);

        controller.RunAction(action);

        // 执行期间抑制计数必须 > 0。
        // 修复前 using var 写在 Task.Run 之外，方法一返回就释放 → 计数始终为 0。
        var field = typeof(QuickLauncher.App.Interaction.InteractionController)
            .GetField("_suppressDepth", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var suppressedDuringRun = false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            if ((int)field.GetValue(controller)! > 0) { suppressedDuringRun = true; break; }
            Thread.Sleep(20);
        }

        Assert.True(suppressedDuringRun, "动作执行期间应处于抑制状态（防止自触发回环）");

        // 执行结束后计数应归零
        var sw2 = Stopwatch.StartNew();
        while (sw2.ElapsedMilliseconds < 3000 && (int)field.GetValue(controller)! > 0)
            Thread.Sleep(20);
        Assert.Equal(0, (int)field.GetValue(controller)!);
    }

    // ─────────────────────────────────────────────────────────────
    // 4) C# 脚本超时：超时必须返回失败，而不是永久阻塞在 task.Result 上
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CSharpScript_Timeout_ReturnsFailureInsteadOfHanging()
    {
        var engine = new CSharpScriptEngine { Timeout = TimeSpan.FromSeconds(2) };
        var ctx = NewContext();

        var sw = Stopwatch.StartNew();
        var outcome = engine.Execute(
            "System.Threading.Thread.Sleep(60000); return 1;",
            new ScriptContext(ctx));
        sw.Stop();

        Assert.False(outcome.Success);
        Assert.Contains("超时", outcome.Error ?? "");
        // 关键：必须真的在超时后返回，而不是卡死
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"实际耗时 {sw.Elapsed}，说明仍然阻塞");
    }

    [Fact]
    public void CSharpScript_NormalExecution_StillWorks()
    {
        var engine = new CSharpScriptEngine { Timeout = TimeSpan.FromSeconds(30) };
        var ctx = NewContext();
        ctx.Set("x", 21);

        var outcome = engine.Execute("return ctx.Get<int>(\"x\") * 2;", new ScriptContext(ctx));

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(42, outcome.Value);
    }

    // ─────────────────────────────────────────────────────────────
    // 5) RunCmd 带 outputVar：UseShellExecute 与重定向输出互斥，否则必崩
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RunCmd_WithOutputVar_CapturesStdout()
    {
        var ctx = NewContext();
        var step = new ActionStep
        {
            StepType = "RunCmd",
            Inputs = { ["shell"] = "cmd", ["command"] = "echo ql_p0_marker", ["outputVar"] = "输出" }
        };

        var impl = StepRegistry.Default.Get("RunCmd")!;
        var result = impl.Execute(ctx, step);

        Assert.True(result.Success, result.Error);
        Assert.Contains("ql_p0_marker", ctx.GetString("输出") ?? "");
    }

    [Fact]
    public void RunCmd_WithoutOutputVar_StillRuns()
    {
        var ctx = NewContext();
        var step = new ActionStep
        {
            StepType = "RunCmd",
            Inputs = { ["shell"] = "cmd", ["command"] = "echo ql_p0_marker" }
        };

        var impl = StepRegistry.Default.Get("RunCmd")!;
        var result = impl.Execute(ctx, step);

        Assert.True(result.Success, result.Error);
    }
}
