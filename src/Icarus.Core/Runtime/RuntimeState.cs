using Icarus.Core.Provider;

namespace Icarus.Core.Runtime;

/// <summary>A snapshot of the runtime's mutable state.</summary>
public sealed record RuntimeState(
    string Model,
    string Provider,
    Effort Effort,
    string Workspace,
    bool Busy);

/// <summary>A discovered skill as the runtime sees it (populated by ICARUS-109).</summary>
public sealed record RuntimeSkill(string Name, string Description, string Path);
