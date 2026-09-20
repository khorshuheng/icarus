using System.Text.Json.Serialization;
using Icarus.Core.Provider;

namespace Icarus.Core.Session;

/// <summary>An unrecoverable session-store problem.</summary>
public sealed class SessionException(string message) : Exception(message);

/// <summary>
/// One JSONL line (ICARUS-106). The header line is <see cref="HeaderEntry"/>;
/// every other line is one conversation message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HeaderEntry), "header")]
[JsonDerivedType(typeof(SystemEntry), "system")]
[JsonDerivedType(typeof(UserEntry), "user")]
[JsonDerivedType(typeof(AssistantEntry), "assistant")]
[JsonDerivedType(typeof(ToolEntry), "tool")]
public abstract record SessionEntry;

/// <summary>The first line: identity and provenance of the session.</summary>
public sealed record HeaderEntry(
    int Version,
    string Id,
    DateTimeOffset CreatedAt,
    string Cwd,
    string? ParentSessionId) : SessionEntry;

/// <summary>A system message.</summary>
public sealed record SystemEntry(string Text) : SessionEntry;

/// <summary>A user message.</summary>
public sealed record UserEntry(string Text) : SessionEntry;

/// <summary>An assistant message (text and/or tool calls).</summary>
public sealed record AssistantEntry(string? Text, IReadOnlyList<ToolCall> ToolCalls) : SessionEntry;

/// <summary>A tool result.</summary>
public sealed record ToolEntry(string ToolCallId, string Result) : SessionEntry;

/// <summary>A session store summary for the <c>/resume</c> picker.</summary>
public sealed record SessionSummary(string Id, string Path, DateTimeOffset CreatedAt, int Messages);
