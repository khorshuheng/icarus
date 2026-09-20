using System.Text.Json.Nodes;

namespace Icarus.Core.Provider;

/// <summary>
/// A tool offered to the provider: its name, one-line description and JSON
/// Schema. The provider needs all three (ICARUS-102); passing bare schemas
/// would lose the name.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonNode Schema);
