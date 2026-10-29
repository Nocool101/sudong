using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickLauncher.Models;
using QuickLauncher.Storage;

namespace QuickLauncher.Engine;

/// <summary>
/// 步骤执行结果（控制流）
/// </summary>
public enum StepFlow
{
    /// <summary>继续下一步</summary>
    Continue,
    /// <summary>跳出当前循环</summary>
    Break,
    /// <summary>继续下一次循环</summary>
    ContinueLoop,
    /// <summary>结束整个动作（return）</summary>
    Return,
    /// <summary>停止所有动作</summary>
    Stop
}

/// <summary>
/// 单步执行结果
/// </summary>
public readonly struct StepResult
{
    public StepFlow Flow { get; }
    public bool Success { get; }
    public string? Error { get; }

    private StepResult(StepFlow flow, bool success, string? error)
    {
        Flow = flow;
        Success = success;
        Error = error;
    }

    public static StepResult Ok() => new(StepFlow.Continue, true, null);
    public static StepResult Ok(StepFlow flow) => new(flow, true, null);
    public static StepResult Fail(string error) => new(StepFlow.Continue, false, error);

    public override string ToString() => Success ? Flow.ToString() : $"Fail({Error})";
}

/// <summary>
/// 动作执行上下文：变量存取、文本插值、持久化变量回写。
/// </summary>
public sealed class StepContext
{
    private readonly Dictionary<string, object?> _vars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _persistent;
    private readonly Guid _actionId;
    private readonly Storage.ActionStore _store;
    private readonly IServiceProvider _services;

    /// <summary>动作输入参数（子程序/文本指令传入）</summary>
    public Dictionary<string, string> InputParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>动作输出参数</summary>
    public Dictionary<string, string> OutputParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取消令牌</summary>
    public System.Threading.CancellationToken CancellationToken { get; init; }

    /// <summary>子步骤执行回调（由 StepExecutor 注入，供容器步骤使用）</summary>
    public Func<ActionStep, StepFlow>? ChildrenRunner { get; set; }

    /// <summary>执行某步骤的子步骤</summary>
    public StepFlow RunChildren(ActionStep step)
        => ChildrenRunner?.Invoke(step) ?? StepFlow.Continue;

    public StepContext(Guid actionId, Storage.ActionStore store, IServiceProvider services)
    {
        _actionId = actionId;
        _store = store;
        _services = services;
        _persistent = store.LoadState(actionId);
        // 持久变量加载到运行时
        foreach (var kv in _persistent)
            _vars[kv.Key] = kv.Value;
    }

    public Guid ActionId => _actionId;
    public IServiceProvider Services => _services;

    #region 变量

    public void Set(string name, object? value)
    {
        _vars[name] = value;
    }

    public T? Get<T>(string name)
    {
        if (!_vars.TryGetValue(name, out var v) || v is null)
            return default;
        if (v is T t) return t;
        try
        {
            return (T?)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return default;
        }
    }

    public string? GetString(string name)
        => _vars.TryGetValue(name, out var v) ? v?.ToString() : null;

    public bool Has(string name) => _vars.ContainsKey(name);

    public IReadOnlyDictionary<string, object?> Variables => _vars;

    #endregion

    #region 文本插值

    // {name} {name:format}
    private static readonly Regex InterpRegex = new(@"\{([^{}:]+)(?::([^{}]+))?\}", RegexOptions.Compiled);

    /// <summary>
    /// 解析文本中的变量插值。支持 {var} 与 {datevar:yyyy-MM-dd}。
    /// </summary>
    public string Interpolate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return InterpRegex.Replace(text, m =>
        {
            var name = m.Groups[1].Value.Trim();
            var format = m.Groups[2].Success ? m.Groups[2].Value : null;
            if (!_vars.TryGetValue(name, out var v) || v is null)
                return m.Value; // 保留原样
            if (format is not null && v is IFormattable f)
                return f.ToString(format, CultureInfo.InvariantCulture);
            return v.ToString() ?? "";
        });
    }

    /// <summary>
    /// 解析参数：先插值；若整个文本就是一个 {var} 且变量存在，返回原始类型值。
    /// </summary>
    public object? ResolveParameter(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var m = Regex.Match(text, @"^\{([^{}:]+)\}$");
        if (m.Success)
        {
            var name = m.Groups[1].Value.Trim();
            if (_vars.TryGetValue(name, out var v))
                return v;
        }
        return Interpolate(text);
    }

    #endregion

    #region 持久化

    /// <summary>标记变量为持久化（写入 state 文件）</summary>
    public void Persist(string name)
    {
        if (_vars.TryGetValue(name, out var v))
            _persistent["$var:" + name] = v?.ToString() ?? "";
    }

    /// <summary>动作结束时调用：把持久化变量写回 state 文件</summary>
    public void CommitPersistent()
    {
        // 只保留带 $var: 前缀的
        var toSave = new Dictionary<string, string>();
        foreach (var kv in _persistent)
        {
            if (kv.Key.StartsWith("$var:"))
                toSave[kv.Key] = kv.Value;
        }
        _store.SaveState(_actionId, toSave);
    }

    #endregion

    #region 表达式

    /// <summary>用当前变量求值表达式</summary>
    public object? Evaluate(string expr)
        => ExpressionEvaluator.Eval(Interpolate(expr), name =>
            _vars.TryGetValue(name, out var v) ? v : null);

    /// <summary>求值布尔表达式</summary>
    public bool EvaluateBool(string expr)
        => ExpressionEvaluator.TryEvaluate(Interpolate(expr), name =>
            _vars.TryGetValue(name, out var v) ? v : null, out var r) && r;

    #endregion
}
