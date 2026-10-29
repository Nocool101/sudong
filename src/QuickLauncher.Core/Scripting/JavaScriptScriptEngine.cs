using System;
using System.Collections.Generic;
using Jint;

namespace QuickLauncher.Scripting;

/// <summary>
/// JavaScript 脚本引擎（Jint）。脚本中可用：
/// - ctx.get(name) / ctx.set(name, value) / ctx.log(msg)
/// - return 返回值
/// </summary>
public sealed class JavaScriptScriptEngine : IScriptEngine
{
    public string Name => "JavaScript";

    public ScriptOutcome Execute(string code, ScriptContext context)
    {
        try
        {
            using var engine = new Jint.Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(30));
            });

            // 注入 ctx 对象
            engine.SetValue("ctx", new JsContextBridge(context));
            engine.SetValue("log", new Action<string>(context.Log));

            var result = engine.Evaluate(code);
            var value = result.ToObject();
            return ScriptOutcome.Ok(value, context.ScriptLog);
        }
        catch (Exception ex)
        {
            return ScriptOutcome.Fail(ex.Message);
        }
    }

    /// <summary>桥接 ScriptContext 到 JS</summary>
    private sealed class JsContextBridge
    {
        private readonly ScriptContext _ctx;
        public JsContextBridge(ScriptContext ctx) => _ctx = ctx;

        public object? get(string name) => _ctx.Get(name);

        public string? getString(string name) => _ctx.GetString(name);

        public void set(string name, object? value) => _ctx.Set(name, value);

        public void log(string message) => _ctx.Log(message);
    }
}
