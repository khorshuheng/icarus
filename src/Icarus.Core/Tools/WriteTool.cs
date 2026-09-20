using System.Text.Json.Nodes;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// The <c>write</c> tool: create or overwrite a regular file. Creates parent
/// directories, refuses non-regular targets, and serializes against
/// <c>edit</c> through <see cref="FileMutationLock"/> (ICARUS-104).
/// </summary>
public sealed class WriteTool : ITool
{
    public string Name => "write";

    public string Description => "Create or overwrite a file with the given content.";

    public JsonNode Schema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "File to create or overwrite.",
            },
            ["content"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Full contents to write.",
            },
        },
        ["required"] = new JsonArray("path", "content"),
    };

    public Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken)
    {
        var path = ToolArgs.RequiredString(args, "path");
        var content = ToolArgs.OptionalString(args, "content")
            ?? throw new ToolArgumentException("'content' is required");

        cancellationToken.ThrowIfCancellationRequested();
        var resolved = workspace.Resolve(path);

        var written = FileMutationLock.Run(resolved, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(resolved))
            {
                throw new ToolInvalidException($"refusing to write '{path}': it is a directory");
            }

            if (File.Exists(resolved) && !IsRegularFile(resolved))
            {
                throw new ToolInvalidException($"refusing to write '{path}': not a regular file");
            }

            var parent = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(resolved, content);
            return content.Length;
        });

        return Task.FromResult(new ToolOutput($"Wrote {written} chars to {path}"));
    }

    private static bool IsRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) == 0;
    }
}
