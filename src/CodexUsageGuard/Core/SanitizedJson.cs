using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageGuard.Core;

public static class SanitizedJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);
}
