using System.Text.Json;
using System.Text.Json.Serialization;

namespace Icarus.Core.Serialization;

/// <summary>
/// The shared JSON options for ICARUS's wire vocabulary (ICARUS-103): snake_case
/// fields, a snake_case enum policy, and nulls omitted. The polymorphic
/// <c>type</c> discriminator comes from the attributes on <c>Event</c>/<c>Command</c>.
/// </summary>
public static class IcarusJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
