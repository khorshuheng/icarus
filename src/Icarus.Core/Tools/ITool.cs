using System.Text.Json.Nodes;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// A built-in tool. Implementations are stateless apart from their output cap.
/// The workspace is a default root, not a sandbox (ICARUS-105).
/// </summary>
public interface ITool
{
    /// <summary>The tool name as the model sees it.</summary>
    string Name { get; }

    /// <summary>A one-line description for the provider's tool definition.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the accepted arguments.</summary>
    JsonNode Schema { get; }

    /// <summary>Run the tool inside <paramref name="workspace"/>.</summary>
    Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken);
}
