using System.Text.Json.Nodes;
using AgentConfig = Icarus.Core.Config.Config;
using Icarus.Core.Provider;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// The four built-in tools, plus argument validation and dispatch
/// (ICARUS-104). There is deliberately no <c>search</c> tool: ICARUS has four.
/// </summary>
public sealed class ToolSet
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolSet(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    /// <summary>The four built-in tools configured from <paramref name="config"/>.</summary>
    public static ToolSet Builtins(AgentConfig config) => new(
    [
        new ReadTool(config.MaxOutputBytes),
        new BashTool(config.MaxOutputBytes, config.BashDefaultTimeout),
        new EditTool(),
        new WriteTool(),
    ]);

    /// <summary>The JSON-Schema tool definitions offered to the provider.</summary>
    public IReadOnlyList<ToolDefinition> Definitions =>
        _tools.Values.Select(t => new ToolDefinition(t.Name, t.Description, t.Schema)).ToArray();

    /// <summary><c>(name, description)</c> for every tool, for the <c>/tools</c> listing.</summary>
    public IReadOnlyList<(string Name, string Description)> Listing =>
        _tools.Values.Select(t => (t.Name, t.Description)).ToArray();

    /// <summary>Look up a tool by name.</summary>
    public ITool? Get(string name) => _tools.GetValueOrDefault(name);

    /// <summary>
    /// Validate <paramref name="args"/> against the tool's schema, then run it.
    /// Malformed model calls never reach an executor.
    /// </summary>
    public Task<ToolOutput> ExecuteAsync(
        Workspace workspace,
        string name,
        JsonNode args,
        CancellationToken cancellationToken)
    {
        var tool = Get(name)
            ?? throw new ToolArgumentException(
                $"unknown tool '{name}' (available: {string.Join(", ", _tools.Keys)})");

        SchemaValidation.Validate(tool.Schema, args);
        return tool.RunAsync(workspace, args, cancellationToken);
    }
}
