using System.Text.Json.Nodes;

namespace Icarus.Core.Tools;

/// <summary>
/// Validates tool arguments against the small JSON-Schema subset the built-in
/// tools declare (ICARUS-104). The tools are a closed set we author, so
/// validating exactly the subset we emit is sufficient and keeps malformed
/// model calls from reaching an executor.
/// </summary>
internal static class SchemaValidation
{
    public static void Validate(JsonNode schema, JsonNode args)
    {
        if (schema["type"]?.GetValue<string>() != "object")
        {
            throw new ToolArgumentException("schema must describe an object");
        }

        if (args is not JsonObject obj)
        {
            throw new ToolArgumentException("arguments must be a JSON object");
        }

        ValidateOneOf(schema, obj);

        if (schema["required"] is JsonArray required)
        {
            foreach (var entry in required)
            {
                var key = entry?.GetValue<string>()
                    ?? throw new ToolArgumentException("schema 'required' entries must be strings");
                if (!obj.ContainsKey(key))
                {
                    throw new ToolArgumentException($"missing required argument '{key}'");
                }
            }
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (key, propertySchema) in properties)
            {
                if (propertySchema is null || !obj.TryGetPropertyValue(key, out var value) || value is null)
                {
                    continue;
                }

                ValidateProperty(key, propertySchema, value);
            }
        }
    }

    private static void ValidateOneOf(JsonNode schema, JsonObject args)
    {
        if (schema["oneOf"] is not JsonArray branches)
        {
            return;
        }

        foreach (var branch in branches)
        {
            if (branch?["required"] is not JsonArray required)
            {
                continue;
            }

            var satisfied = required.All(entry =>
                args.ContainsKey(entry?.GetValue<string>() ?? string.Empty));
            if (satisfied)
            {
                return;
            }
        }

        var names = branches
            .Select(b => b?["required"] is JsonArray r
                ? string.Join(" + ", r.Select(e => e?.GetValue<string>() ?? "?"))
                : "?")
            .ToArray();
        throw new ToolArgumentException(
            $"one of these argument sets is required: {string.Join(" | ", names)}");
    }

    private static void ValidateProperty(string key, JsonNode schema, JsonNode value)
    {
        if (schema["oneOf"] is JsonArray alternatives)
        {
            foreach (var alternative in alternatives)
            {
                var type = alternative?["type"]?.GetValue<string>();
                if (MatchesType(type, value))
                {
                    ValidateProperty(key, alternative!, value);
                    return;
                }
            }

            throw new ToolArgumentException($"'{key}' has an unsupported type");
        }

        var expected = schema["type"]?.GetValue<string>();
        if (expected is not null && !MatchesType(expected, value))
        {
            throw new ToolArgumentException($"'{key}' must be {expected}");
        }

        if (value is JsonValue scalar && scalar.TryGetValue<int>(out var number))
        {
            if (schema["minimum"] is JsonValue min && number < min.GetValue<int>())
            {
                throw new ToolArgumentException($"'{key}' must be >= {min.GetValue<int>()}");
            }

            if (schema["maximum"] is JsonValue max && number > max.GetValue<int>())
            {
                throw new ToolArgumentException($"'{key}' must be <= {max.GetValue<int>()}");
            }
        }

        if (expected == "array" && value is JsonArray array && schema["items"] is JsonNode itemSchema)
        {
            foreach (var item in array)
            {
                if (item is null)
                {
                    continue;
                }

                if (itemSchema["type"]?.GetValue<string>() == "object")
                {
                    Validate(itemSchema, item);
                }
                else if (!MatchesType(itemSchema["type"]?.GetValue<string>(), item))
                {
                    throw new ToolArgumentException($"'{key}' entries must be {itemSchema["type"]?.GetValue<string>()}");
                }
            }
        }
    }

    private static bool MatchesType(string? type, JsonNode value) => type switch
    {
        null => true,
        "string" => value is JsonValue v && v.TryGetValue<string>(out _),
        "integer" => value is JsonValue n && n.TryGetValue<int>(out _),
        "boolean" => value is JsonValue b && b.TryGetValue<bool>(out _),
        "array" => value is JsonArray,
        "object" => value is JsonObject,
        _ => false,
    };
}
