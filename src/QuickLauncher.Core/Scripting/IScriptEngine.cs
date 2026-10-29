using System;
using System.Collections.Generic;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Scripting;

/// <summary>
/// 脚本引擎接口。实现 C#/JS/Python 代码步骤。
/// </summary>
public interface IScriptEngine
{
    /// <summary>引擎名称（CSharp/JavaScript/Python）</summary>
    string Name { get; }

    /// <summary>执行脚本，返回结果值</summary>
    ScriptOutcome Execute(string code, ScriptContext context);
}

/// <summary>
/// 脚本执行结果
/// </summary>
public sealed class ScriptOutcome
{
    public bool Success { get; init; }
    public object? Value { get; init; }
    public string? Error { get; init; }
    public List<string> Log { get; init; } = new();

    public static ScriptOutcome Ok(object? value, List<string>? log = null)
        => new() { Success = true, Value = value, Log = log ?? new() };

    public static ScriptOutcome Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// 脚本上下文：变量读写 + 日志
/// </summary>
public sealed class ScriptContext
{
    private readonly StepContext _ctx;

    public ScriptContext(StepContext ctx)
    {
        _ctx = ctx;
    }

    public object? Get(string name) => _ctx.Variables.TryGetValue(name, out var v) ? v : null;

    public string? GetString(string name) => _ctx.GetString(name);

    public T? Get<T>(string name) => _ctx.Get<T>(name);

    public void Set(string name, object? value) => _ctx.Set(name, value);

    public void Log(string message) => ScriptLog.Add(message);

    public List<string> ScriptLog { get; } = new();

    /// <summary>输入参数（子程序/指令传入）</summary>
    public IReadOnlyDictionary<string, string> Parameters => _ctx.InputParameters;
}
