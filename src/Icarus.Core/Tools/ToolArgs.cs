using System.Text.Json.Nodes;

namespace Icarus.Core.Tools;

/// <summary>Helpers for reading model-supplied tool arguments defensively.</summary>
internal static class ToolArgs
{
    /// <summary>A required non-empty string argument.</summary>
    public static string RequiredString(JsonNode? args, string name)
    {
        var value = OptionalString(args, name);
        if (string.IsNullOrEmpty(value))
        {
            throw new ToolArgumentException($"'{name}' is required");
        }

        return value;
    }

    /// <summary>An optional string argument.</summary>
    public static string? OptionalString(JsonNode? args, string name)
    {
        if (args is not JsonObject obj || !obj.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw new ToolArgumentException($"'{name}' must be a string");
        }

        return text;
    }

    /// <summary>An optional integer argument, range-checked when bounds are given.</summary>
    public static int? OptionalInt(JsonNode? args, string name, int? min = null, int? max = null)
    {
        if (args is not JsonObject obj || !obj.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue<int>(out var number))
        {
            throw new ToolArgumentException($"'{name}' must be an integer");
        }

        if (min is not null && number < min)
        {
            throw new ToolArgumentException($"'{name}' must be >= {min}");
        }

        if (max is not null && number > max)
        {
            throw new ToolArgumentException($"'{name}' must be <= {max}");
        }

        return number;
    }

    /// <summary>A string-or-array-of-strings argument, normalized to a list.</summary>
    public static IReadOnlyList<string> StringOrArray(JsonNode? args, string name)
    {
        if (args is not JsonObject obj || !obj.TryGetPropertyValue(name, out var node) || node is null)
        {
            return [];
        }

        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var single):
                return [single];
            case JsonArray array:
                var result = new List<string>(array.Count);
                foreach (var item in array)
                {
                    if (item is not JsonValue entry || !entry.TryGetValue<string>(out var text))
                    {
                        throw new ToolArgumentException($"'{name}' entries must be strings");
                    }

                    result.Add(text);
                }

                return result;
            default:
                throw new ToolArgumentException($"'{name}' must be a string or an array of strings");
        }
    }
}
