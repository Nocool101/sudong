using System;
using System.Globalization;
using System.Text;

namespace QuickLauncher.Engine;

/// <summary>
/// 轻量布尔/算术表达式求值器（递归下降）。
/// 支持：and/or/not、比较、算术、括号、字符串与数字字面量。
/// </summary>
public static class ExpressionEvaluator
{
    public static bool TryEvaluate(string expr, out bool result)
    {
        try
        {
            var v = Eval(expr);
            result = ToBool(v);
            return true;
        }
        catch
        {
            result = false;
            return false;
        }
    }

    /// <summary>带变量解析的求值</summary>
    public static bool TryEvaluate(string expr, Func<string, object?>? resolver, out bool result)
    {
        try
        {
            var v = Eval(expr, resolver);
            result = ToBool(v);
            return true;
        }
        catch
        {
            result = false;
            return false;
        }
    }

    public static bool EvalBool(string expr)
        => TryEvaluate(expr, out var r) && r;

    public static object Eval(string expr)
    {
        var parser = new Parser(expr);
        return parser.ParseExpression();
    }

    public static object Eval(string expr, Func<string, object?>? resolver)
    {
        var parser = new Parser(expr) { VariableResolver = resolver };
        return parser.ParseExpression();
    }

    private static bool ToBool(object? v)
    {
        return v switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0",
            double d => d != 0,
            _ => true
        };
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _pos;

        /// <summary>变量解析器（标识符 -> 值），为空时标识符仅限 true/false</summary>
        public Func<string, object?>? VariableResolver { get; init; }

        public Parser(string s) { _s = s; }

        public object ParseExpression()
        {
            var v = ParseOr();
            SkipWs();
            if (_pos < _s.Length) throw new FormatException($"意外字符: {_s[_pos]}");
            return v;
        }

        private object ParseOr()
        {
            var left = ParseAnd();
            while (true)
            {
                SkipWs();
                if (MatchKeyword("or") || Match("||"))
                {
                    var right = ParseAnd();
                    left = ToBool(left) || ToBool(right);
                }
                else break;
            }
            return left;
        }

        private object ParseAnd()
        {
            var left = ParseNot();
            while (true)
            {
                SkipWs();
                if (MatchKeyword("and") || Match("&&"))
                {
                    var right = ParseNot();
                    left = ToBool(left) && ToBool(right);
                }
                else break;
            }
            return left;
        }

        private object ParseNot()
        {
            SkipWs();
            if (MatchKeyword("not") || Match("!"))
                return !ToBool(ParseNot());
            return ParseComparison();
        }

        private object ParseComparison()
        {
            var left = ParseAdditive();
            while (true)
            {
                SkipWs();
                if (Match("==")) left = Equals_(left, ParseAdditive());
                else if (Match("!=")) left = !Equals_(left, ParseAdditive());
                else if (Match(">=")) left = Compare(left, ParseAdditive()) >= 0;
                else if (Match("<=")) left = Compare(left, ParseAdditive()) <= 0;
                else if (Match(">")) left = Compare(left, ParseAdditive()) > 0;
                else if (Match("<")) left = Compare(left, ParseAdditive()) < 0;
                else break;
            }
            return left;
        }

        private object ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWs();
                if (Match("+")) left = Arith(left, ParseMultiplicative(), (a, b) => a + b);
                else if (Match("-")) left = Arith(left, ParseMultiplicative(), (a, b) => a - b);
                else break;
            }
            return left;
        }

        private object ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWs();
                if (Match("*")) left = Arith(left, ParseUnary(), (a, b) => a * b);
                else if (Match("/")) left = Arith(left, ParseUnary(), (a, b) => b == 0 ? 0 : a / b);
                else break;
            }
            return left;
        }

        private object ParseUnary()
        {
            SkipWs();
            if (Match("-")) return -ToNum(ParseUnary());
            return ParsePrimary();
        }

        private object ParsePrimary()
        {
            SkipWs();
            if (_pos >= _s.Length) throw new FormatException("表达式意外结束");
            var c = _s[_pos];
            if (c == '(')
            {
                _pos++;
                var v = ParseOr();
                SkipWs();
                if (_pos >= _s.Length || _s[_pos] != ')') throw new FormatException("缺少 )");
                _pos++;
                return v;
            }
            if (c == '"' || c == '\'')
            {
                var quote = c;
                _pos++;
                var sb = new StringBuilder();
                while (_pos < _s.Length && _s[_pos] != quote)
                {
                    if (_s[_pos] == '\\' && _pos + 1 < _s.Length) _pos++;
                    sb.Append(_s[_pos]);
                    _pos++;
                }
                if (_pos >= _s.Length) throw new FormatException("字符串未闭合");
                _pos++;
                return sb.ToString();
            }
            if (char.IsDigit(c) || c == '.')
            {
                var start = _pos;
                while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
                return double.Parse(_s.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = _pos;
                while (_pos < _s.Length && (char.IsLetterOrDigit(_s[_pos]) || _s[_pos] == '_')) _pos++;
                var word = _s.Substring(start, _pos - start);
                var lower = word.ToLowerInvariant();
                if (lower == "true") return true;
                if (lower == "false") return false;
                if (VariableResolver is not null)
                {
                    var v = VariableResolver(word);
                    if (v is not null) return v;
                }
                throw new FormatException($"未知标识符: {word}");
            }
            throw new FormatException($"无法解析: {_s[_pos]}");
        }

        private void SkipWs()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        private bool Match(string op)
        {
            SkipWs();
            if (_pos + op.Length <= _s.Length && string.CompareOrdinal(_s, _pos, op, 0, op.Length) == 0)
            {
                _pos += op.Length;
                return true;
            }
            return false;
        }

        private bool MatchKeyword(string kw)
        {
            SkipWs();
            if (_pos + kw.Length > _s.Length) return false;
            if (string.Compare(_s, _pos, kw, 0, kw.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            // 前后必须是边界
            if (_pos > 0 && (char.IsLetterOrDigit(_s[_pos - 1]) || _s[_pos - 1] == '_')) return false;
            var end = _pos + kw.Length;
            if (end < _s.Length && (char.IsLetterOrDigit(_s[end]) || _s[end] == '_')) return false;
            _pos = end;
            return true;
        }

        private static bool Equals_(object? a, object? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (IsNum(a) && IsNum(b)) return ToNum(a) == ToNum(b);
            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        private static int Compare(object? a, object? b)
        {
            if (IsNum(a) && IsNum(b)) return ToNum(a).CompareTo(ToNum(b));
            return string.CompareOrdinal(a?.ToString(), b?.ToString());
        }

        private static double Arith(object? a, object? b, Func<double, double, double> op)
        {
            if (a is string sa && b is null) return a is null ? 0 : 0; // 字符串拼接简化场景不支持
            return op(ToNum(a), ToNum(b));
        }

        private static bool IsNum(object? v) => v is double or int or long or float;

        private static double ToNum(object? v) => v switch
        {
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            bool b => b ? 1 : 0,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }
}
