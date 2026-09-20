using System.Text.Json.Serialization;
using Icarus.Core.Provider;

namespace Icarus.Core.Runtime;

/// <summary>
/// The frozen command vocabulary (ICARUS-103, copied from CRAB-116). The
/// serialized form uses a snake_case <c>type</c> discriminator; the optional
/// correlation <c>id</c> is reserved for a request/response transport (ICARUS
/// has none with the TUI-only frontend).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PromptCommand), "prompt")]
[JsonDerivedType(typeof(SteerCommand), "steer")]
[JsonDerivedType(typeof(FollowUpCommand), "follow_up")]
[JsonDerivedType(typeof(AbortCommand), "abort")]
[JsonDerivedType(typeof(SetModelCommand), "set_model")]
[JsonDerivedType(typeof(SetProviderCommand), "set_provider")]
[JsonDerivedType(typeof(SetEffortCommand), "set_effort")]
[JsonDerivedType(typeof(SwitchWorkspaceCommand), "switch_workspace")]
[JsonDerivedType(typeof(ClearCommand), "clear")]
[JsonDerivedType(typeof(GetStateCommand), "get_state")]
[JsonDerivedType(typeof(ListModelsCommand), "list_models")]
[JsonDerivedType(typeof(ResumeCommand), "resume")]
public abstract record Command;

/// <summary>Start a new turn with the given user message.</summary>
public sealed record PromptCommand(string Text) : Command;

/// <summary>Queue a steering message (delivered at the next completion boundary).</summary>
public sealed record SteerCommand(string Text) : Command;

/// <summary>Queue a follow-up (delivered when the agent settles).</summary>
public sealed record FollowUpCommand(string Text) : Command;

/// <summary>Cancel the in-flight turn immediately, keeping partial text.</summary>
public sealed record AbortCommand : Command;

/// <summary>Change the model used for subsequent completions.</summary>
public sealed record SetModelCommand(string Model) : Command;

/// <summary>Switch the active provider at runtime.</summary>
public sealed record SetProviderCommand(string Provider) : Command;

/// <summary>Change the thinking level.</summary>
public sealed record SetEffortCommand(Effort Effort) : Command;

/// <summary>Change the workspace and re-seed the system prompt.</summary>
public sealed record SwitchWorkspaceCommand(string Path) : Command;

/// <summary>Reset the conversation to a fresh context.</summary>
public sealed record ClearCommand : Command;

/// <summary>Ask the runtime to report its state.</summary>
public sealed record GetStateCommand : Command;

/// <summary>Discover the provider's available models.</summary>
public sealed record ListModelsCommand : Command;

/// <summary>Reload the previous session (handled by the frontend's session store).</summary>
public sealed record ResumeCommand : Command;
