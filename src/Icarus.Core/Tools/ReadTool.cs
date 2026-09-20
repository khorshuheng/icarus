using System.Text;
using System.Text.Json.Nodes;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// The <c>read</c> tool: read one file, a list of paths, or a directory/glob
/// set, with an optional line range (ICARUS-104).
/// </summary>
public sealed class ReadTool(int maxOutputBytes) : ITool
{
    /// <summary>Lines returned per file.</summary>
    public const int MaxLines = 2000;

    /// <summary>Files returned per call.</summary>
    public const int MaxFiles = 32;

    /// <summary>Files at or below this size are read whole; larger files stream.</summary>
    public const long MaxFullRead = 4 * 1024 * 1024;

    private const int LongLineHint = 2000;

    public string Name => "read";

    public string Description => "Read one or more files in the workspace, optionally by line range.";

    public JsonNode Schema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["type"] = "string" },
                    new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }),
                ["description"] = "File(s) to read, relative to the workspace. Pass a directory with 'glob'.",
            },
            ["glob"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["type"] = "string" },
                    new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }),
                ["description"] = "When 'path' is a directory, files matching these glob(s); a leading '!' excludes.",
            },
            ["offset"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["description"] = "1-indexed starting line (applies per file).",
            },
            ["limit"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["description"] = "Number of lines to return per file.",
            },
        },
        ["required"] = new JsonArray("path"),
    };

    public Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken)
    {
        var paths = ToolArgs.StringOrArray(args, "path");
        if (paths.Count == 0)
        {
            throw new ToolArgumentException("'path' is required");
        }

        var globs = ToolArgs.StringOrArray(args, "glob");
        var offset = ToolArgs.OptionalInt(args, "offset", min: 1) ?? 1;
        var limit = ToolArgs.OptionalInt(args, "limit", min: 1);

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = workspace.Resolve(path);

            if (Directory.Exists(resolved))
            {
                if (globs.Count == 0)
                {
                    throw new ToolInvalidException($"'{path}' is a directory; pass 'glob' to read matching files");
                }

                foreach (var file in TreeWalk.Files(resolved, globs))
                {
                    if (seen.Add(file))
                    {
                        files.Add(file);
                    }
                }
            }
            else if (File.Exists(resolved))
            {
                if (seen.Add(resolved))
                {
                    files.Add(resolved);
                }
            }
            else
            {
                throw new ToolNotFoundException(path);
            }
        }

        files.Sort(StringComparer.Ordinal);
        var capped = false;
        if (files.Count > MaxFiles)
        {
            files = files[..MaxFiles];
            capped = true;
        }

        if (files.Count == 0)
        {
            return Task.FromResult(new ToolOutput("(no files matched)"));
        }

        var multi = files.Count > 1;
        var output = new StringBuilder();
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            var display = Display(workspace.Root, file);

            if (multi)
            {
                if (i > 0)
                {
                    output.Append('\n');
                }

                output.Append($"==> {display} <==\n");
            }

            output.Append(ReadOne(file, offset, limit, cancellationToken)).Append('\n');
        }

        var text = output.ToString().TrimEnd();
        if (capped)
        {
            text += $"\n\n[stopped after {MaxFiles} files; narrow the glob]";
        }

        if (text.Length > maxOutputBytes)
        {
            text = TextUtil.Head(text, maxOutputBytes);
            text += "\n\n[output truncated; use offset/limit or a narrower glob]";
        }

        return Task.FromResult(new ToolOutput(text));
    }

    private static string Display(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return relative.StartsWith("..", StringComparison.Ordinal) ? file : relative;
    }

    private static string ReadOne(string path, int offset, int? limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new FileInfo(path).Length <= MaxFullRead
            ? ReadWhole(path, offset, limit)
            : ReadStreaming(path, offset, limit, cancellationToken);
    }

    private static string ReadWhole(string path, int offset, int? limit)
    {
        var lines = File.ReadAllLines(path);
        var total = lines.Length;
        if (offset > total)
        {
            return $"offset {offset} is beyond end of file ({total} lines total)";
        }

        var maxLines = Math.Min(limit ?? MaxLines, MaxLines);
        var count = Math.Min(maxLines, total - (offset - 1));
        var slice = lines.AsSpan(offset - 1, count);
        var body = string.Join('\n', slice.ToArray());

        var hint = new StringBuilder();
        var shownEnd = offset + count - 1;
        if (shownEnd < total)
        {
            hint.Append($"\n\n[Showing lines {offset}-{shownEnd} of {total}. Use offset={shownEnd + 1} to continue.]");
        }

        if (slice.ToArray().FirstOrDefault(line => line.Length > LongLineHint) is { } longLine)
        {
            hint.Append($"\n\n[some lines are very long ({longLine.Length} chars); read a range with offset/limit]");
        }

        return body + hint;
    }

    private static string ReadStreaming(string path, int offset, int? limit, CancellationToken cancellationToken)
    {
        var maxLines = Math.Min(limit ?? MaxLines, MaxLines);
        var selected = new List<string>(maxLines);
        var lineNumber = 0;
        var hasMore = false;

        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (lineNumber < offset)
            {
                continue;
            }

            if (selected.Count >= maxLines)
            {
                hasMore = true;
                break;
            }

            selected.Add(line);
        }

        if (lineNumber < offset)
        {
            return $"offset {offset} is beyond end of file ({lineNumber} lines total)";
        }

        var body = string.Join('\n', selected);
        var shownEnd = offset + selected.Count - 1;
        return hasMore
            ? $"{body}\n\n[Showing lines {offset}-{shownEnd}. Use offset={shownEnd + 1} to continue.]"
            : body;
    }
}
