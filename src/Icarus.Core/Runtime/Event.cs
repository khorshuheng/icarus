using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Icarus.Core.Provider;

namespace Icarus.Core.Runtime;

/// <summary>
/// The frozen event vocabulary (ICARUS-103, copied from CRAB-116). Serialized
/// with a snake_case <c>type</c> discriminator via
/// <see cref="Serialization.IcarusJson"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AgentStartEvent), "agent_start")]
[JsonDerivedType(typeof(TurnStartEvent), "turn_start")]
[JsonDerivedType(typeof(TextDeltaEvent), "text_delta")]
[JsonDerivedType(typeof(ThinkingDeltaEvent), "thinking_delta")]
[JsonDerivedType(typeof(ToolStartEvent), "tool_start")]
[JsonDerivedType(typeof(ToolEndEvent), "tool_end")]
[JsonDerivedType(typeof(TurnEndEvent), "turn_end")]
[JsonDerivedType(typeof(UsageEvent), "usage")]
[JsonDerivedType(typeof(QueueUpdateEvent), "queue_update")]
[JsonDerivedType(typeof(StateChangedEvent), "state_changed")]
[JsonDerivedType(typeof(AgentSettledEvent), "agent_settled")]
[JsonDerivedType(typeof(ModelsListedEvent), "models_listed")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract record Event;

/// <summary>The runtime started; carries the initial identity.</summary>
public sealed record AgentStartEvent(string Model, Effort Effort, string Workspace) : Event;

/// <summary>A new user message is about to be processed.</summary>
public sealed record TurnStartEvent : Event;

/// <summary>A streamed fragment of assistant text.</summary>
public sealed record TextDeltaEvent(string Text) : Event;

/// <summary>A streamed fragment of model reasoning.</summary>
public sealed record ThinkingDeltaEvent(string Text) : Event;

/// <summary>A tool call is about to execute (args are raw model-supplied arguments).</summary>
public sealed record ToolStartEvent(string Name, string? Id, JsonNode? Args) : Event;

/// <summary>A tool call finished.</summary>
public sealed record ToolEndEvent(string Name, bool Ok, string? Error) : Event;

/// <summary>An assistant turn finished.</summary>
public sealed record TurnEndEvent : Event;

/// <summary>Token usage reported by the last completion.</summary>
public sealed record UsageEvent(int? PromptTokens) : Event;

/// <summary>The command queue changed (a steer/follow-up was queued while busy).</summary>
public sealed record QueueUpdateEvent(int Queued, string Kind) : Event;

/// <summary>Runtime state changed (model/effort/workspace/provider).</summary>
public sealed record StateChangedEvent(string Model, string Provider, Effort Effort, string Workspace) : Event;

/// <summary>The agent settled: a final answer, or empty when cancelled.</summary>
public sealed record AgentSettledEvent(string Text, bool Interrupted) : Event;

/// <summary>The provider's available model ids.</summary>
public sealed record ModelsListedEvent(IReadOnlyList<string> Models) : Event;

/// <summary>A non-fatal error surfaced by the runtime.</summary>
public sealed record ErrorEvent(string Message) : Event;
