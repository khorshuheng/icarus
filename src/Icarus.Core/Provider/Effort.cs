namespace Icarus.Core.Provider;

/// <summary>
/// The canonical thinking level, mapped per provider (ICARUS-102). <c>Off</c>
/// disables thinking; the other levels request progressively more reasoning.
/// <c>Medium</c> is the default.
/// </summary>
public enum Effort
{
    Medium = 0,
    Off = 1,
    Minimal = 2,
    Low = 3,
    High = 4,
}

/// <summary>Parsing and wire-name helpers for <see cref="Effort"/>.</summary>
public static class EffortExtensions
{
    /// <summary>The canonical level name (the serialized form).</summary>
    public static string Name(this Effort effort) => effort switch
    {
        Effort.Off => "off",
        Effort.Minimal => "minimal",
        Effort.Low => "low",
        Effort.Medium => "medium",
        Effort.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };

    /// <summary>Parse a canonical level name (case-insensitive), or <c>null</c>.</summary>
    public static Effort? Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "off" => Effort.Off,
        "minimal" => Effort.Minimal,
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => null,
    };
}
