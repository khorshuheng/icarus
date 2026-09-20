using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// The <c>edit</c> tool: apply precise, validated text replacements to a file
/// (ICARUS-104). Accepts a batch <c>edits</c> array or a single
/// <c>oldText</c>/<c>newText</c>. Matching is exact first, then fuzzy — NFKC,
/// smart quotes/dashes/Unicode spaces, and per-line trailing whitespace — with
/// duplicate detection in the normalized space.
/// </summary>
public sealed class EditTool : ITool
{
    public string Name => "edit";

    public string Description => "Apply precise text replacements to a file.";

    public JsonNode Schema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File to edit." },
            ["edits"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["oldText"] = new JsonObject { ["type"] = "string" },
                        ["newText"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("oldText", "newText"),
                },
                ["description"] = "Disjoint replacements applied in order.",
            },
            ["oldText"] = new JsonObject { ["type"] = "string", ["description"] = "Alternative to 'edits' for a single replacement." },
            ["newText"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("path"),
        ["oneOf"] = new JsonArray(
            new JsonObject { ["required"] = new JsonArray("edits") },
            new JsonObject { ["required"] = new JsonArray("oldText", "newText") }),
    };

    public Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken)
    {
        var path = ToolArgs.RequiredString(args, "path");
        var edits = ParseEdits(args);
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = workspace.Resolve(path);
        if (!File.Exists(resolved))
        {
            throw new ToolNotFoundException(path);
        }

        var message = FileMutationLock.Run(resolved, () => Apply(path, resolved, edits, cancellationToken));
        return Task.FromResult(new ToolOutput(message));
    }

    private static IReadOnlyList<(string Old, string New)> ParseEdits(JsonNode args)
    {
        if (args["edits"] is JsonArray array)
        {
            if (array.Count == 0)
            {
                throw new ToolArgumentException("'edits' must not be empty");
            }

            var result = new List<(string, string)>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject entry)
                {
                    throw new ToolArgumentException($"edits[{i}] must be an object");
                }

                result.Add((
                    Required(entry, "oldText", $"edits[{i}].oldText"),
                    Required(entry, "newText", $"edits[{i}].newText")));
            }

            return result;
        }

        return [(
            ToolArgs.OptionalString(args, "oldText")
                ?? throw new ToolArgumentException("'oldText' is required"),
            ToolArgs.OptionalString(args, "newText")
                ?? throw new ToolArgumentException("'newText' is required"))];
    }

    private static string Required(JsonObject entry, string key, string label) =>
        entry.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : throw new ToolArgumentException($"'{label}' is required");

    private static string Apply(
        string displayPath,
        string resolved,
        IReadOnlyList<(string Old, string New)> edits,
        CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(resolved);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var encoding = new UTF8Encoding(false);
        var original = encoding.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var ending = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lf = original.Replace("\r\n", "\n", StringComparison.Ordinal);

        var (normalized, map) = Normalize(lf);
        var ranges = new List<(int Start, int End, string New)>(edits.Count);

        foreach (var (oldText, newText) in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldNormalized = Normalize(oldText).Norm;
            if (oldNormalized.Length == 0)
            {
                throw new ToolInvalidException("oldText must not be empty");
            }

            var normStart = FindUnique(normalized, oldNormalized, displayPath);
            var lfStart = map[normStart].Start;
            var last = map[normStart + oldNormalized.Length - 1];
            ranges.Add((lfStart, last.Start + last.Length, newText));
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
            {
                throw new ToolInvalidException("edits overlap; they must be disjoint");
            }
        }

        var builder = new StringBuilder();
        var cursor = 0;
        foreach (var (start, end, replacement) in ranges)
        {
            builder.Append(lf, cursor, start - cursor);
            builder.Append(replacement);
            cursor = end;
        }

        builder.Append(lf, cursor, lf.Length - cursor);
        var updated = builder.ToString();

        var text = updated.Replace("\n", ending, StringComparison.Ordinal);
        if (hasBom)
        {
            text = "\uFEFF" + text;
        }

        File.WriteAllText(resolved, text, encoding);
        return $"Edited {displayPath}\n\n{UnifiedDiff(lf, updated)}";
    }

    private static int FindUnique(string normalized, string needle, string path)
    {
        var first = normalized.IndexOf(needle, StringComparison.Ordinal);
        if (first < 0)
        {
            throw new ToolInvalidException($"oldText was not found in '{path}'");
        }

        if (normalized.IndexOf(needle, first + 1, StringComparison.Ordinal) >= 0)
        {
            throw new ToolInvalidException($"oldText matches more than once in '{path}'; include more context");
        }

        return first;
    }

    /// <summary>Normalize for matching, returning the string and a source-range map.</summary>
    internal static (string Norm, List<(int Start, int Length)> Map) Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var map = new List<(int Start, int Length)>(text.Length);

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var start = enumerator.ElementIndex;
            var folded = element.Normalize(NormalizationForm.FormKC);
            foreach (var c in folded)
            {
                builder.Append(Fuzzy(c));
                map.Add((start, element.Length));
            }
        }

        return TrimTrailingWhitespace(builder.ToString(), map);
    }

    private static char Fuzzy(char c) => c switch
    {
        '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
        '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
        '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
        '\u00A0' or '\u202F' or '\u205F' or '\u3000' => ' ',
        >= '\u2000' and <= '\u200A' => ' ',
        _ => c,
    };

    private static (string, List<(int Start, int Length)>) TrimTrailingWhitespace(
        string text,
        List<(int Start, int Length)> map)
    {
        var builder = new StringBuilder(text.Length);
        var trimmedMap = new List<(int Start, int Length)>(map.Count);
        var i = 0;

        while (i < text.Length)
        {
            var lineEnd = text.IndexOf('\n', i);
            var end = lineEnd < 0 ? text.Length : lineEnd;
            var contentEnd = end;
            while (contentEnd > i && text[contentEnd - 1] is ' ' or '\t' or '\r')
            {
                contentEnd--;
            }

            for (var k = i; k < contentEnd; k++)
            {
                builder.Append(text[k]);
                trimmedMap.Add(map[k]);
            }

            if (lineEnd >= 0)
            {
                builder.Append('\n');
                trimmedMap.Add(map[lineEnd]);
                i = lineEnd + 1;
            }
            else
            {
                i = text.Length;
            }
        }

        return (builder.ToString(), trimmedMap);
    }

    /// <summary>A minimal unified diff (no context) over the changed region.</summary>
    internal static string UnifiedDiff(string before, string after)
    {
        var oldLines = before.Split('\n');
        var newLines = after.Split('\n');

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length
            && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var removed = oldLines[prefix..(oldLines.Length - suffix)];
        var added = newLines[prefix..(newLines.Length - suffix)];

        var builder = new StringBuilder();
        builder.Append($"@@ -{prefix + 1},{removed.Length} +{prefix + 1},{added.Length} @@\n");
        foreach (var line in removed)
        {
            builder.Append('-').Append(line).Append('\n');
        }

        foreach (var line in added)
        {
            builder.Append('+').Append(line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
