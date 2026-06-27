using System.Text.Json;

namespace Trackarr.Core;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public static string? Serialize<T>(T? value)
    {
        if (value is null) return null;

        if (value is JsonElement element) return element.GetRawText();

        return JsonSerializer.Serialize(value, Options);
    }
}
