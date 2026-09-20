namespace Icarus.Core.Tools;

/// <summary>The result of running a tool: text fed back to the model.</summary>
public sealed record ToolOutput(string Content);

/// <summary>A typed tool failure. The runtime formats it as a tool error and feeds it back.</summary>
public abstract class ToolException : Exception
{
    protected ToolException(string message) : base(message) { }
}

/// <summary>Missing or malformed argument.</summary>
public sealed class ToolArgumentException(string message) : ToolException(message);

/// <summary>The referenced file does not exist.</summary>
public sealed class ToolNotFoundException(string message) : ToolException(message);

/// <summary>The operation is invalid (e.g. an edit matched zero or many times).</summary>
public sealed class ToolInvalidException(string message) : ToolException(message);

/// <summary>An I/O failure.</summary>
public sealed class ToolIoException(string message) : ToolException(message);

/// <summary>A command failed (non-zero exit or killed by a signal).</summary>
public sealed class ToolCommandException(string message) : ToolException(message);

/// <summary>A command exceeded its timeout.</summary>
public sealed class ToolTimeoutException(string message) : ToolException(message);

/// <summary>The tool was interrupted by a cancellation request.</summary>
public sealed class ToolCancelledException() : ToolException("cancelled");
