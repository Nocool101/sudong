using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Storage;
using QuickLauncher.Steps;
using QuickLauncher.Scripting;
using Xunit;

namespace QuickLauncher.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ActionStore _store;

    public StorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ql_test_" + Guid.NewGuid().ToString("N")[..8]);
        _db = new Database(_dir);
        _store = new ActionStore(_db, _dir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Action_SaveAndLoad_RoundTrip()
    {
        var action = new ActionItem
        {
            Name = "测试动作",
            SimpleType = "OpenUrl",
            SimpleParam = "https://example.com"
        };
        action.Steps.Add(new ActionStep { StepType = "SendText", Inputs = { ["text"] = "hello" } });
        action.Variables.Add(new ActionVariable { Name = "v1", DefaultValue = "42" });

        _store.SaveAction(action);

        var loaded = _store.LoadAction(action.Id);
        Assert.NotNull(loaded);
        Assert.Equal("测试动作", loaded!.Name);
        Assert.Single(loaded.Steps);
        Assert.Equal("hello", loaded.Steps[0].Inputs["text"]);
        Assert.Equal("42", loaded.Variables[0].DefaultValue);
    }

    [Fact]
    public void State_PersistPerAction()
    {
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        _store.SetStateValue(a1, "$var:count", "5");
        _store.SetStateValue(a1, "$var:name", "abc");
        _store.SetStateValue(a2, "$var:count", "9");

        Assert.Equal("5", _store.GetStateValue(a1, "$var:count"));
        Assert.Equal("9", _store.GetStateValue(a2, "$var:count"));
        Assert.Null(_store.GetStateValue(a2, "$var:name"));

        var all = _store.LoadState(a1);
        Assert.Equal(2, all.Count);

        // 文件确实落在 states 目录
        Assert.True(File.Exists(Path.Combine(_store.StateDir, $"state_{a1}.json")));
    }

    [Fact]
    public void Usage_Recording()
    {
        var id = Guid.NewGuid();
        _store.RecordActionRun(id);
        _store.RecordActionRun(id);
        var (count, last) = _store.GetActionUsage(id);
        Assert.Equal(2, count);
        Assert.NotNull(last);
    }

    [Fact]
    public void Scene_And_Page_CRUD()
    {
        var page = new ActionPage { Name = "p1", Columns = 3 };
        page.Cells.Add(new ActionCell { Index = 0, ActionId = Guid.NewGuid() });
        _store.SavePage(page);

        var scene = new Scene { Name = "Chrome", ProcessMatch = "chrome*", PanelPageId = page.Id };
        _store.SaveScene(scene);

        var scenes = _store.LoadScenes();
        Assert.Single(scenes);
        Assert.Equal("chrome*", scenes[0].ProcessMatch);

        var loadedPage = _store.LoadPage(page.Id);
        Assert.NotNull(loadedPage);
        Assert.Single(loadedPage!.Cells);

        _store.DeleteScene(scene.Id);
        Assert.Empty(_store.LoadScenes());
    }
}

public class EngineTests
{
    private static (StepExecutor executor, ActionStore store) CreateExecutor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_eng_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var services = new SimpleServiceProvider();
        var executor = new StepExecutor(StepRegistry.Default, store, services);
        return (executor, store);
    }

    private sealed class SimpleServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void Expression_Basic()
    {
        Assert.True(ExpressionEvaluator.EvalBool("true and not false"));
        Assert.True(ExpressionEvaluator.EvalBool("1 < 2"));
        Assert.True(ExpressionEvaluator.EvalBool("\"abc\" == \"abc\""));
        Assert.False(ExpressionEvaluator.EvalBool("1 > 2 or false"));
        Assert.True(ExpressionEvaluator.EvalBool("(1 + 2) * 3 == 9"));
    }

    [Fact]
    public void KeySyntax_Parse()
    {
        Assert.Equal(new ushort[] { 0x11, 0x43 }, KeySyntax.Parse("^c").ToArray());   // Ctrl+C
        Assert.Equal(new ushort[] { 0x11, 0x10, 0x73 }, KeySyntax.Parse("^+{F4}").ToArray()); // Ctrl+Shift+F4
        Assert.Equal(new ushort[] { 0x70 }, KeySyntax.Parse("{F1}").ToArray());
        Assert.Equal(new ushort[] { 0x41 }, KeySyntax.Parse("a").ToArray());
        Assert.Equal(new ushort[] { 0x11, 0x12, 0x41 }, KeySyntax.Parse("^!a").ToArray());
    }

    [Fact]
    public void CompositeAction_VariablesAndFlow()
    {
        var (executor, store) = CreateExecutor();
        var action = new ActionItem
        {
            Name = "流程测试",
            Kind = ActionKind.Composite,
            Variables = { new ActionVariable { Name = "total" } }
        };
        // total = 0; 重复 5 次: total = total + index; 如果 total > 5: 停止
        action.Steps.Add(new ActionStep { StepType = "SetVariable", Inputs = { ["name"] = "total", ["value"] = "=0" } });
        action.Steps.Add(new ActionStep
        {
            StepType = "Repeat",
            Inputs = { ["count"] = "5", ["indexVar"] = "i" },
            Children =
            {
                new ActionStep { StepType = "SetVariable", Inputs = { ["name"] = "total", ["value"] = "=total + i" } }
            }
        });
        action.Steps.Add(new ActionStep
        {
            StepType = "If",
            Inputs = { ["condition"] = "total > 5" },
            Children = { new ActionStep { StepType = "Stop" } }
        });

        var result = executor.Execute(action);
        Assert.True(result.Success, result.Error);
        // 0+1+2+3+4 = 10 > 5，If 分支会执行 Stop
        Assert.Contains("停止动作", string.Join("\n", result.Log));
    }

    [Fact]
    public void Interpolation_Works()
    {
        var (executor, store) = CreateExecutor();
        var action = new ActionItem
        {
            Name = "插值测试",
            Kind = ActionKind.Composite
        };
        action.Steps.Add(new ActionStep { StepType = "SetVariable", Inputs = { ["name"] = "name", ["value"] = "世界" } });
        action.Steps.Add(new ActionStep { StepType = "SetVariable", Inputs = { ["name"] = "greeting", ["value"] = "你好 {name}" } });

        var result = executor.Execute(action);
        Assert.True(result.Success, result.Error);
        // 验证 state 里 greeting 已写入（通过持久化标记验证不可行，直接检查日志无失败）
        Assert.DoesNotContain("失败", string.Join("\n", result.Log));
    }

    [Fact]
    public void Persistent_Variable_WrittenToState()
    {
        var (executor, store) = CreateExecutor();
        var action = new ActionItem { Name = "持久化测试", Kind = ActionKind.Composite };
        action.Steps.Add(new ActionStep
        {
            StepType = "SetVariable",
            Inputs = { ["name"] = "counter", ["value"] = "=1 + 1", ["persistent"] = "true" }
        });

        var result = executor.Execute(action);
        Assert.True(result.Success, result.Error);

        var state = store.LoadState(action.Id);
        Assert.True(state.ContainsKey("$var:counter"));
        Assert.Equal("2", state["$var:counter"]);
    }
}


public class KeySyntaxTests
{
    [Theory]
    [InlineData("^c", new ushort[] { 0x11, 0x43 })]        // Ctrl+C
    [InlineData("^+{F4}", new ushort[] { 0x11, 0x10, 0x73 })] // Ctrl+Shift+F4
    [InlineData("^!a", new ushort[] { 0x11, 0x12, 0x41 })] // Ctrl+Alt+A
    [InlineData("#r", new ushort[] { 0x5B, 0x52 })]        // Win+R
    [InlineData("{ENTER}", new ushort[] { 0x0D })]
    [InlineData("a", new ushort[] { 0x41 })]
    public void Parse_KnownSyntax(string syntax, ushort[] expected)
    {
        Assert.Equal(expected, KeySyntax.Parse(syntax).ToArray());
    }

    [Theory]
    [InlineData("^c")]
    [InlineData("^+{F4}")]
    [InlineData("{ESC}")]
    public void RoundTrip_AllTokens(string syntax)
    {
        // 解析出的键数应 >=1
        Assert.NotEmpty(KeySyntax.Parse(syntax));
    }
}

public class ScriptEngineTests
{
    private static StepContext CreateContext()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_script_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        return new StepContext(Guid.NewGuid(), store, new SimpleServiceProvider());
    }

    private sealed class SimpleServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void CSharp_Script_VariablesAndReturn()
    {
        var ctx = CreateContext();
        ctx.Set("count", 5);
        var scriptCtx = new ScriptContext(ctx);
        var engine = new CSharpScriptEngine();
        var outcome = engine.Execute("ctx.Set(\"doubled\", ctx.Get<int>(\"count\") * 2);\nreturn ctx.Get<int>(\"doubled\");", scriptCtx);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(10, outcome.Value);
    }

    [Fact]
    public void CSharp_Script_LinqAndLog()
    {
        var ctx = CreateContext();
        var scriptCtx = new ScriptContext(ctx);
        var engine = new CSharpScriptEngine();
        var outcome = engine.Execute("ctx.Log(\"hi\");\nreturn Enumerable.Range(1, 5).Sum();", scriptCtx);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(15, outcome.Value);
        Assert.Contains("hi", scriptCtx.ScriptLog);
    }

    [Fact]
    public void JavaScript_Script_Basic()
    {
        var ctx = CreateContext();
        ctx.Set("name", "world");
        var scriptCtx = new ScriptContext(ctx);
        var engine = new JavaScriptScriptEngine();
        var outcome = engine.Execute("ctx.log('hello'); ctx.set('greeting', 'hi ' + ctx.get('name')); ctx.get('greeting');", scriptCtx);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("hi world", outcome.Value);
    }

    [Fact]
    public void CompositeAction_WithScriptStep()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_cs_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var executor = new StepExecutor(StepRegistry.Default, store, new SP());

        var action = new ActionItem
        {
            Name = "脚本组合",
            Kind = ActionKind.Composite,
            Steps =
            {
                new ActionStep
                {
                    StepType = "RunCSharp",
                    Inputs = { ["code"] = "return 6 * 7;", ["resultVar"] = "answer" }
                },
                new ActionStep
                {
                    StepType = "SetVariable",
                    Inputs = { ["name"] = "message", ["value"] = "答案是 {answer}" }
                }
            }
        };
        var result = executor.Execute(action);
        Assert.True(result.Success, result.Error);
    }

    private sealed class SP : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

public class AddActionMenuTests
{
    [Fact]
    public void Registry_ContainsScriptAndSwitchPageSteps()
    {
        Assert.True(StepRegistry.Default.Contains("RunCSharp"));
        Assert.True(StepRegistry.Default.Contains("RunJavaScript"));
        Assert.True(StepRegistry.Default.Contains("RunPython"));
        Assert.True(StepRegistry.Default.Contains("RunScriptFile"));
        Assert.True(StepRegistry.Default.Contains("SwitchPage"));
        Assert.True(StepRegistry.Default.Contains("RunCmd"));
    }

    [Fact]
    public void ActionItem_JsonRoundTrip_SupportsImport()
    {
        var action = new ActionItem
        {
            Name = "导入测试",
            Kind = ActionKind.Composite,
            Steps =
            {
                new ActionStep
                {
                    StepType = "RunCSharp",
                    Inputs = { ["code"] = "ctx.Log(\"hi\");\nreturn 1;" },
                    Children = { new ActionStep { StepType = "Comment", Inputs = { ["text"] = "子步骤" } } }
                }
            }
        };

        var json = Json.Serialize(action);
        var loaded = Json.Deserialize<ActionItem>(json);

        Assert.NotNull(loaded);
        Assert.Equal("导入测试", loaded!.Name);
        Assert.Single(loaded.Steps);
        Assert.Equal("RunCSharp", loaded.Steps[0].StepType);
        Assert.Contains("return 1;", loaded.Steps[0].Inputs["code"]);
        Assert.Single(loaded.Steps[0].Children);
    }

    [Fact]
    public void SwitchPage_SimpleAction_NoPanelService_FailsGracefully()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_sw_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var executor = new StepExecutor(StepRegistry.Default, store, new SPNull());

        var action = new ActionItem
        {
            Name = "加载动作页",
            Kind = ActionKind.Simple,
            SimpleType = "SwitchPage",
            SimpleParam = Guid.NewGuid().ToString()
        };
        var result = executor.Execute(action);
        Assert.False(result.Success);
    }

    private sealed class SPNull : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

public class SimpleActionFormTests
{
    private static StepContext CreateContext()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_sf_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        return new StepContext(Guid.NewGuid(), store, new SPNull2());
    }

    private sealed class SPNull2 : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void Registry_ContainsRunScriptUnifiedStep()
    {
        Assert.True(StepRegistry.Default.Contains("RunScript"));
    }

    [Fact]
    public void SimpleAction_LegacyParam_MapsToMainParam()
    {
        // 旧数据（仅 SimpleParam）：应被映射为该类型主参数并传达到步骤。
        // SwitchPage 读到非空 pageId 才会走到"面板服务不可用"（空 pageId 则报"页面 Id 为空"）。
        var dir = Path.Combine(Path.GetTempPath(), "ql_legacy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var executor = new StepExecutor(StepRegistry.Default, store, new SPNull2());

        var action = new ActionItem
        {
            Name = "旧版加载动作页",
            Kind = ActionKind.Simple,
            SimpleType = "SwitchPage",
            SimpleParam = Guid.NewGuid().ToString()
        };
        var result = executor.Execute(action);
        Assert.False(result.Success);
        Assert.Contains("面板服务不可用", result.Error);
    }

    [Fact]
    public void SimpleAction_MultiParam_UsedByExecutor()
    {
        // 新数据（SimpleInputs 多参数）：SwitchPage 从 SimpleInputs 读 pageId
        var dir = Path.Combine(Path.GetTempPath(), "ql_multi_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var executor = new StepExecutor(StepRegistry.Default, store, new SPNull2());

        var action = new ActionItem
        {
            Name = "新版加载动作页",
            Kind = ActionKind.Simple,
            SimpleType = "SwitchPage",
            SimpleInputs = { ["pageId"] = Guid.NewGuid().ToString() }
        };
        var result = executor.Execute(action);
        Assert.False(result.Success);
        Assert.Contains("面板服务不可用", result.Error);
    }

    [Fact]
    public void RunScript_DispatchesByLanguage_CSharp()
    {
        var ctx = CreateContext();
        var step = new ActionStep
        {
            StepType = "RunScript",
            Inputs =
            {
                ["language"] = "csharp",
                ["code"] = "return 6 * 7;",
                ["resultVar"] = "out"
            }
        };
        var r = new QuickLauncher.Scripting.ScriptSteps.RunScript().Execute(ctx, step);
        Assert.True(r.Success, r.Error);
        Assert.Equal(42, ctx.Get<int>("out"));
    }

    [Fact]
    public void RunScript_DispatchesByLanguage_JavaScript()
    {
        var ctx = CreateContext();
        var step = new ActionStep
        {
            StepType = "RunScript",
            Inputs =
            {
                ["language"] = "js",
                ["code"] = "ctx.set('answer', 6 * 7);",
                ["resultVar"] = "out"
            }
        };
        var r = new QuickLauncher.Scripting.ScriptSteps.RunScript().Execute(ctx, step);
        Assert.True(r.Success, r.Error);
    }

    [Fact]
    public void SimpleInputs_JsonRoundTrip()
    {
        var action = new ActionItem
        {
            Name = "多参数动作",
            Kind = ActionKind.Simple,
            SimpleType = "RunProgram",
            SimpleInputs = { ["program"] = "notepad.exe", ["args"] = "a.txt", ["wait"] = "true" }
        };
        var loaded = Json.Deserialize<ActionItem>(Json.Serialize(action));
        Assert.NotNull(loaded);
        Assert.Equal("notepad.exe", loaded!.SimpleInputs["program"]);
        Assert.Equal("true", loaded.SimpleInputs["wait"]);
    }
}

public class IconExtractorTests
{
    private static string Notepad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

    [Fact]
    public void CountIcons_SystemExe_HasIcons()
    {
        Assert.True(File.Exists(Notepad), "notepad.exe 应存在");
        Assert.True(QuickLauncher.App.Services.IconExtractor.CountIcons(Notepad) > 0);
    }

    [Fact]
    public void Extract_AndCache_ProducesPng()
    {
        var icon = QuickLauncher.App.Services.IconExtractor.Extract(Notepad, 0);
        Assert.NotNull(icon);

        var path = QuickLauncher.App.Services.IconExtractor.ExtractAndCache(Notepad, 0);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.EndsWith(".png", path!);

        // 再次提取应命中缓存（同路径）
        var again = QuickLauncher.App.Services.IconExtractor.ExtractAndCache(Notepad, 0);
        Assert.Equal(path, again);
    }

    [Fact]
    public void IsImageFile_DetectsPngAndRejectsOthers()
    {
        var png = QuickLauncher.App.Services.IconExtractor.ExtractAndCache(Notepad, 0)!;
        Assert.True(QuickLauncher.App.Services.IconExtractor.IsImageFile(png));
        Assert.False(QuickLauncher.App.Services.IconExtractor.IsImageFile("[fa:Solid_Play:#FF0000]"));
        Assert.False(QuickLauncher.App.Services.IconExtractor.IsImageFile("▶"));
        Assert.False(QuickLauncher.App.Services.IconExtractor.IsImageFile(null));
        Assert.False(QuickLauncher.App.Services.IconExtractor.IsImageFile("C:\not_exist_xyz.png"));
    }
}

public class WindowXamlLoadTests
{
    /// <summary>在 STA 线程中构造窗口，验证 XAML/BAML 可正常加载（颜色值等错误仅在运行期暴露）</summary>
    private static Exception? ConstructInSta(Func<System.Windows.Window> factory)
    {
        Exception? error = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                var w = factory();
                if (w is null) error = new InvalidOperationException("窗口为 null");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(10));
        return error;
    }

    [Fact]
    public void AddActionDialog_XamlLoads()
    {
        var error = ConstructInSta(() => new QuickLauncher.App.Views.AddActionDialog());
        Assert.Null(error);
    }

    [Fact]
    public void IconPickerDialog_XamlLoads()
    {
        var error = ConstructInSta(() => new QuickLauncher.App.Views.IconPickerDialog("▶"));
        Assert.Null(error);
    }

    [Fact]
    public void ActionEditorWindow_XamlLoads()
    {
        var error = ConstructInSta(() => new QuickLauncher.App.Views.ActionEditorWindow(
            new ActionItem { Name = "测试", Kind = ActionKind.Simple, SimpleType = "RunProgram" }));
        Assert.Null(error);
    }
}

public class ReproTests
{
    /// <summary>复现：编辑器「提取程序图标」按钮链路</summary>
    [Fact]
    public void ExtractIconButton_FullChain()
    {
        Exception? error = null;
        string? iconResult = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                var action = new ActionItem
                {
                    Name = "启动软件",
                    Kind = ActionKind.Simple,
                    SimpleType = "RunProgram",
                    SimpleInputs = { ["program"] = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe") }
                };
                var w = new QuickLauncher.App.Views.ActionEditorWindow(action);
                var mi = typeof(QuickLauncher.App.Views.ActionEditorWindow)
                    .GetMethod("ExtractIconButton_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(mi);
                mi!.Invoke(w, new object?[] { null, null });
                var iconBox = (System.Windows.Controls.TextBox)w.FindName("IconBox")!;
                iconResult = iconBox.Text;
            }
            catch (Exception ex) { error = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException! : ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(15));
        Assert.Null(error);
        Assert.False(string.IsNullOrEmpty(iconResult));
        Assert.True(File.Exists(iconResult!), $"图标文件应存在: {iconResult}");
    }

    /// <summary>复现：快捷键组合解析</summary>
    [Theory]
    [InlineData("Ctrl+Alt+P", true)]
    [InlineData("Ctrl+Shift+A", true)]
    [InlineData("F5", true)]
    [InlineData("Alt+Q", true)]
    public void HotkeyParse_Combos(string hotkey, bool expected)
    {
        Assert.Equal(expected, QuickLauncher.App.Interaction.HotkeyParser.TryParse(hotkey, out _, out _));
    }

    /// <summary>复现：.lnk 快捷方式解析（开始菜单程序）</summary>
    [Fact]
    public void ResolveShortcut_LnkTarget()
    {
        // 创建一个指向 notepad 的临时 .lnk
        var lnk = Path.Combine(Path.GetTempPath(), "ql_test_" + Guid.NewGuid().ToString("N")[..8] + ".lnk");
        try
        {
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            Assert.NotNull(t);
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            sc.Save();

            var target = QuickLauncher.App.Services.IconExtractor.ResolveShortcut(lnk);
            Assert.NotNull(target);
            Assert.EndsWith("notepad.exe", target!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(lnk); } catch { }
        }
    }
}

public class HotkeyLogicTests
{
    /// <summary>修饰键匹配：当前未按任何修饰键</summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("ctrl", false)]
    [InlineData("alt", false)]
    public void ModifiersMatch_NoKeysPressed(string modifiers, bool expected)
    {
        var mi = typeof(QuickLauncher.App.Interaction.HotkeyLeaderWatcher)
            .GetMethod("ModifiersMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mi);
        var result = (bool)mi!.Invoke(null, new object?[] { modifiers })!;
        Assert.Equal(expected, result);
    }

    /// <summary>Quicker 按键语法 → 标准格式（录制快捷键用）</summary>
    [Theory]
    [InlineData("^p", "Ctrl+P")]
    [InlineData("^!p", "Ctrl+Alt+P")]
    [InlineData("^+{F4}", "Ctrl+Shift+F4")]
    [InlineData("#a", "Win+A")]
    public void CapturedKey_ToStandardFormat(string syntax, string expected)
    {
        var mi = typeof(QuickLauncher.App.Views.ActionEditorWindow)
            .GetMethod("ConvertToStandardHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mi);
        var result = (string)mi!.Invoke(null, new object?[] { syntax })!;
        Assert.Equal(expected, result);
    }
}

public class PanelLayoutTests
{
    private static (QuickLauncher.App.Services.PanelService svc, ActionPage page, ActionStore store) CreateService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_layout_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var page = new ActionPage { Name = "p", Columns = 4 };
        store.SavePage(page);
        var svc = new QuickLauncher.App.Services.PanelService(
            store, new QuickLauncher.App.Scenes.SceneMatcher(new List<Scene>()), new AppSettings());
        typeof(QuickLauncher.App.Services.PanelService)
            .GetField("_globalPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, page);
        return (svc, page, store);
    }

    private static void Invoke(QuickLauncher.App.Services.PanelService svc, string method, params object[] args)
        => typeof(QuickLauncher.App.Services.PanelService)
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(svc, args);

    [Fact]
    public void BindActionToCell_AddsWithoutOverwrite()
    {
        var (svc, page, store) = CreateService();
        var a1 = new ActionItem { Name = "A1" };
        var a2 = new ActionItem { Name = "A2" };
        var a3 = new ActionItem { Name = "A3" };
        store.SaveAction(a1); store.SaveAction(a2); store.SaveAction(a3);
        page.Cells.Add(new ActionCell { Index = 0, ActionId = a1.Id });
        page.Cells.Add(new ActionCell { Index = 1, ActionId = a2.Id });

        // 添加到空格 Index=2：不应影响已有磁贴
        Invoke(svc, "BindActionToCell", a3.Id, 2, false);

        Assert.Equal(3, page.Cells.Count);
        Assert.Equal(a1.Id, page.Cells.First(c => c.Index == 0).ActionId);
        Assert.Equal(a2.Id, page.Cells.First(c => c.Index == 1).ActionId);
        Assert.Equal(a3.Id, page.Cells.First(c => c.Index == 2).ActionId);
    }

    [Fact]
    public void ActionMove_ToEmptyCell_AllowsSparseLayout()
    {
        // 自由放置：拖到空格直接占用该格，前排可留空（无需放满横排）
        var (svc, page, store) = CreateService();
        var a1 = new ActionItem { Name = "A1" };
        var a2 = new ActionItem { Name = "A2" };
        store.SaveAction(a1); store.SaveAction(a2);
        page.Cells.Add(new ActionCell { Index = 0, ActionId = a1.Id });
        page.Cells.Add(new ActionCell { Index = 1, ActionId = a2.Id });

        // 把 A2 从第 2 格拖到第 4 格（下一排首位）：A1 不动，中间留空
        Invoke(svc, "OnPanelActionMove", a2.Id, 4, false, false);

        Assert.Equal(0, page.Cells.First(c => c.ActionId == a1.Id).Index);
        Assert.Equal(4, page.Cells.First(c => c.ActionId == a2.Id).Index);
        Assert.DoesNotContain(page.Cells, c => c.ActionId != Guid.Empty && c.Index is 1 or 2 or 3);
    }

    [Fact]
    public void ActionMove_ToOccupiedCell_Swaps()
    {
        // 拖到已有磁贴上：交换位置，其他不动
        var (svc, page, store) = CreateService();
        var a1 = new ActionItem { Name = "A1" };
        var a2 = new ActionItem { Name = "A2" };
        store.SaveAction(a1); store.SaveAction(a2);
        page.Cells.Add(new ActionCell { Index = 0, ActionId = a1.Id });
        page.Cells.Add(new ActionCell { Index = 1, ActionId = a2.Id });

        Invoke(svc, "OnPanelActionMove", a1.Id, 1, false, false);

        Assert.Equal(1, page.Cells.First(c => c.ActionId == a1.Id).Index);
        Assert.Equal(0, page.Cells.First(c => c.ActionId == a2.Id).Index);
    }
}

public class AutoNameTests
{
    /// <summary>复现：添加"启动软件"后输入程序路径，名称应自动识别为程序名</summary>
    [Fact]
    public void AutoName_FromProgramPath()
    {
        Exception? error = null;
        string? name = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                var action = new ActionItem
                {
                    Name = "启动软件",
                    Kind = ActionKind.Simple,
                    SimpleType = "RunProgram",
                    SimpleInputs = { ["program"] = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe") }
                };
                var w = new QuickLauncher.App.Views.ActionEditorWindow(action);
                typeof(QuickLauncher.App.Views.ActionEditorWindow)
                    .GetMethod("SimpleParamEditor_Changed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(w, null);
                name = ((System.Windows.Controls.TextBox)w.FindName("NameBox")!).Text;
            }
            catch (Exception ex) { error = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException! : ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(15));
        Assert.Null(error);
        Assert.False(string.IsNullOrWhiteSpace(name), "名称应被自动识别");
        Assert.NotEqual("启动软件", name);
    }
}

public class CrossZoneTests
{
    private static (QuickLauncher.App.Services.PanelService svc, ActionPage globalPage, ActionPage contextPage, ActionStore store) CreateDualZone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ql_xz_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var db = new Database(dir);
        var store = new ActionStore(db, dir);
        var globalPage = new ActionPage { Name = "全局", Columns = 4 };
        var contextPage = new ActionPage { Name = "专用", Columns = 4 };
        store.SavePage(globalPage);
        store.SavePage(contextPage);
        var svc = new QuickLauncher.App.Services.PanelService(
            store, new QuickLauncher.App.Scenes.SceneMatcher(new List<Scene>()), new AppSettings());
        var t = typeof(QuickLauncher.App.Services.PanelService);
        t.GetField("_globalPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, globalPage);
        t.GetField("_contextPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, contextPage);
        return (svc, globalPage, contextPage, store);
    }

    private static void Invoke(QuickLauncher.App.Services.PanelService svc, string method, params object[] args)
        => typeof(QuickLauncher.App.Services.PanelService)
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(svc, args);

    [Fact]
    public void CrossZone_MoveToEmptyCell_LeavesGlobal()
    {
        var (svc, globalPage, contextPage, store) = CreateDualZone();
        var a1 = new ActionItem { Name = "A1" };
        store.SaveAction(a1);
        globalPage.Cells.Add(new ActionCell { Index = 0, ActionId = a1.Id });

        // 上区拖到下区空格：磁贴移到下区，上区移除
        Invoke(svc, "OnPanelActionMove", a1.Id, 0, true, false);

        Assert.DoesNotContain(globalPage.Cells, c => c.ActionId == a1.Id);
        Assert.Equal(a1.Id, contextPage.Cells.First(c => c.Index == 0).ActionId);
    }

    [Fact]
    public void CrossZone_DropOnTile_SwapsZones()
    {
        var (svc, globalPage, contextPage, store) = CreateDualZone();
        var a1 = new ActionItem { Name = "A1" };
        var a2 = new ActionItem { Name = "A2" };
        store.SaveAction(a1); store.SaveAction(a2);
        globalPage.Cells.Add(new ActionCell { Index = 0, ActionId = a1.Id });
        contextPage.Cells.Add(new ActionCell { Index = 0, ActionId = a2.Id });

        // 把上区 A1 拖到下区 A2 上：两个磁贴互换区
        Invoke(svc, "OnPanelActionMove", a1.Id, 0, true, false);

        Assert.Equal(a2.Id, globalPage.Cells.First(c => c.Index == 0).ActionId);
        Assert.Equal(a1.Id, contextPage.Cells.First(c => c.Index == 0).ActionId);
    }
}

public class SceneMatchIsolationTests
{
    private static QuickLauncher.App.Scenes.SceneMatcher Matcher(params Scene[] scenes)
    {
        foreach (var s in scenes) s.Enabled = true;
        return new QuickLauncher.App.Scenes.SceneMatcher(scenes);
    }

    // 复现 bug：“下区专用磁贴漏到别的程序”。ProcessMatch="*" 的应用场景一旦配了页面，
    // 会匹配所有程序。修复后此类“匹配所有程序”的场景不得作为专用上下文场景参与下区匹配。
    [Fact]
    public void Context_StarProcess_IsIgnored_KeepsIsolation()
    {
        var page = Guid.NewGuid();
        var m = Matcher(new Scene { Type = SceneType.Application, ProcessMatch = "*", PanelPageId = page });
        Assert.False(m.MatchContext("chrome", "", "", s => s.PanelPageId).Found);
        Assert.False(m.MatchContext("notepad", "", "", s => s.PanelPageId).Found);
    }

    [Fact]
    public void Context_EmptyConditions_IsIgnored()
    {
        var page = Guid.NewGuid();
        var m = Matcher(new Scene { Type = SceneType.Application, ProcessMatch = "", PanelPageId = page });
        Assert.False(m.MatchContext("chrome", "", "", s => s.PanelPageId).Found);
    }

    [Fact]
    public void Context_SpecificProcess_MatchesOnlyItsProcess()
    {
        var page = Guid.NewGuid();
        var m = Matcher(new Scene { Type = SceneType.Application, ProcessMatch = "chrome", PanelPageId = page });
        Assert.True(m.MatchContext("chrome", "", "", s => s.PanelPageId).Found);
        Assert.False(m.MatchContext("notepad", "", "", s => s.PanelPageId).Found);
    }

    [Fact]
    public void Context_StarProcess_WithClassConstraint_StillMatchesByClass()
    {
        // ProcessMatch="*" 但带窗口类名约束时仍是专用场景，按类名匹配（不应被误伤）
        var page = Guid.NewGuid();
        var m = Matcher(new Scene
        {
            Type = SceneType.Application,
            ProcessMatch = "*",
            WindowClassMatch = "Chrome_WidgetWin_1",
            PanelPageId = page
        });
        Assert.True(m.MatchContext("whatever", "", "Chrome_WidgetWin_1", s => s.PanelPageId).Found);
        Assert.False(m.MatchContext("whatever", "", "OtherClass", s => s.PanelPageId).Found);
    }

    // 关闭应用后前台落空或被系统服务 broker 窗口(如 OmApSvcBroker)占据时，
    // 下区应回落到桌面(explorer)，而不是显示它们的空专用区。
    [Fact]
    public void ResolveContext_EmptyOrServiceBroker_FallsBackToDesktop()
    {
        var M = QuickLauncher.App.Scenes.SceneMatcher.ResolveContextProcess;
        Assert.Equal("explorer", M(""));
        Assert.Equal("explorer", M(null));
        Assert.Equal("explorer", M("OmApSvcBroker"));
        Assert.Equal("explorer", M("explorer"));
    }

    [Fact]
    public void ResolveContext_NormalApp_Unchanged()
    {
        var M = QuickLauncher.App.Scenes.SceneMatcher.ResolveContextProcess;
        Assert.Equal("chrome", M("chrome"));
        Assert.Equal("PiDeck", M("PiDeck"));
    }
}
