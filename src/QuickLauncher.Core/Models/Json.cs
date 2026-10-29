using System.IO;
using Newtonsoft.Json;

namespace QuickLauncher.Models;

/// <summary>
/// 模型深拷贝扩展（基于 JSON 序列化）
/// </summary>
public static class CloneExtensions
{
    public static T Clone<T>(this T obj)
    {
        var json = Json.Serialize(obj);
        return Json.Deserialize<T>(json)!;
    }
}

/// <summary>
/// JSON 序列化帮助类（统一配置）
/// </summary>
public static class Json
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

    public static string Serialize(object? value)
    {
        using var sw = new StringWriter();
        using var jw = new JsonTextWriter(sw);
        Serializer.Serialize(jw, value);
        return sw.ToString();
    }

    public static T? Deserialize<T>(string json)
    {
        using var sr = new StringReader(json);
        using var jr = new JsonTextReader(sr);
        return Serializer.Deserialize<T>(jr);
    }
}
