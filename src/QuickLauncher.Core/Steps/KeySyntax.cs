using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickLauncher.Engine;

namespace QuickLauncher.Steps;

/// <summary>
/// Quicker 风格按键语法解析：
/// ^ = Ctrl, ! = Alt, + = Shift, # = Win
/// 示例：^c → Ctrl+C, ^+{ESC}, {F1}, a, ^+s
/// </summary>
public static class KeySyntax
{
    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = 0x0D, ["RETURN"] = 0x0D, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["TAB"] = 0x09, ["SPACE"] = 0x20, ["BACKSPACE"] = 0x08, ["BS"] = 0x08,
        ["DELETE"] = 0x2E, ["DEL"] = 0x2E, ["INSERT"] = 0x2D, ["INS"] = 0x2D,
        ["HOME"] = 0x24, ["END"] = 0x23, ["PGUP"] = 0x21, ["PAGEUP"] = 0x21,
        ["PGDN"] = 0x22, ["PAGEDOWN"] = 0x22, ["UP"] = 0x26, ["DOWN"] = 0x28,
        ["LEFT"] = 0x25, ["RIGHT"] = 0x27, ["CAPSLOCK"] = 0x14,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    /// <summary>
    /// 解析按键语法为按键序列
    /// </summary>
    public static List<ushort> Parse(string syntax)
    {
        var result = new List<ushort>();
        if (string.IsNullOrWhiteSpace(syntax)) return result;

        // 分离前缀修饰符与主体
        var body = syntax;
        var prefixes = new List<ushort>();
        while (body.Length > 0 && "^!+#".Contains(body[0]))
        {
            switch (body[0])
            {
                case '^': prefixes.Add(0x11); break; // VK_CONTROL
                case '!': prefixes.Add(0x12); break; // VK_MENU
                case '+': prefixes.Add(0x10); break; // VK_SHIFT
                case '#': prefixes.Add(0x5B); break; // VK_LWIN
            }
            body = body[1..];
        }

        // {命名键} 或单字符
        if (body.Length >= 2 && body.StartsWith("{") && body.EndsWith("}"))
        {
            var name = body[1..^1];
            if (NamedKeys.TryGetValue(name, out var vk))
                result.Add(vk);
            else if (name.Length == 1)
                result.Add(char.ToUpperInvariant(name[0]));
        }
        else if (body.Length == 1)
        {
            result.Add(char.ToUpperInvariant(body[0]));
        }
        else if (NamedKeys.TryGetValue(body, out var named))
        {
            result.Add(named);
        }

        return prefixes.Concat(result).ToList();
    }

    /// <summary>发送按键语法</summary>
    public static void Send(string syntax)
    {
        var keys = Parse(syntax);
        foreach (var k in keys) Win32.SendKey(k, true);
        foreach (var k in keys.AsEnumerable().Reverse()) Win32.SendKey(k, false);
    }
}
