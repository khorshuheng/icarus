using System.Text.Json.Nodes;

namespace Icarus.Core.Provider;

/// <summary>A tool call requested by the model.</summary>
public sealed record ToolCall(string Id, string Name, JsonNode Args);

/// <summary>
/// A canonical message in the agent history. This is ICARUS's own type (not an
/// MEAI type): providers convert to and from it, and the loop, budgeting and
/// tests are built on it (ICARUS-101).
/// </summary>
public abstract record Message
{
    /// <summary>The system prompt (always history[0]).</summary>
    public sealed record System(string Text) : Message;

    /// <summary>A user message (a prompt, a steer, a follow-up, or a skill).</summary>
    public sealed record User(string Text) : Message;

    /// <summary>
    /// An assistant message: final text, tool calls, or both. <see cref="Text"/>
    /// is <c>null</c> when the message carried only tool calls.
    /// </summary>
    public sealed record Assistant(string? Text, IReadOnlyList<ToolCall> ToolCalls) : Message
    {
        public static Assistant OfText(string text) => new(text, []);

        public static Assistant OfToolCalls(IReadOnlyList<ToolCall> calls) => new(null, calls);
    }

    /// <summary>The result of executing one tool call, fed back verbatim.</summary>
    public sealed record ToolResult(string ToolCallId, string Result) : Message;
}

/// <summary>The outcome of a completion: final text, or one or more tool calls.</summary>
public abstract record Response
{
    /// <summary>A final answer.</summary>
    public sealed record Text(string Value) : Response;

    /// <summary>One or more tool calls to execute.</summary>
    public sealed record ToolCalls(IReadOnlyList<ToolCall> Calls) : Response;

    /// <summary>
    /// Tool calls cut off by the output token limit. Their arguments may be
    /// incomplete and must never be executed (ICARUS-102).
    /// </summary>
    public sealed record TruncatedToolCalls(IReadOnlyList<ToolCall> Calls) : Response;
}

/// <summary>
/// A provider completion: the response plus the exact prompt/input token count
/// reported by the provider. <see cref="PromptTokens"/> anchors context
/// budgeting and is <c>null</c> when the provider does not report usage (never
/// guessed).
/// </summary>
/// <param name="Response">The final response.</param>
/// <param name="PromptTokens">Provider-reported input tokens, or <c>null</c>.</param>
/// <param name="Aborted">True when generation was interrupted mid-stream.</param>
public sealed record Completion(Response Response, int? PromptTokens, bool Aborted);

/// <summary>
/// A streamed fragment of a completion: assistant text (<c>IsThinking</c>
/// false) or model reasoning (<c>IsThinking</c> true, rendered dim italic).
/// </summary>
public readonly record struct StreamDelta(string Text, bool IsThinking)
{
    /// <summary>An assistant text fragment.</summary>
    public static StreamDelta Content(string text) => new(text, false);

    /// <summary>A model reasoning/thinking fragment.</summary>
    public static StreamDelta Thinking(string text) => new(text, true);
}
