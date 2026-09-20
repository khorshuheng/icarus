namespace Icarus.Core.Runtime;

/// <summary>A terminal runtime failure (iteration cap, provider failure, interrupt).</summary>
public sealed class RuntimeException(string message) : Exception(message);
