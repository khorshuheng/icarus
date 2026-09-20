using Icarus.Core.Theme;

namespace Icarus.Core.Config;

/// <summary>
/// One source of configuration (the config file or CLI flags). Every field is
/// optional; unresolved fields fall back to the next-lower-precedence source
/// and then to defaults. There is deliberately no <c>api_key</c> field
/// (ICARUS-105).
/// </summary>
public sealed record PartialConfig
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Region { get; init; }
    public string? BaseUrl { get; init; }
    public float? Temperature { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxOutputBytes { get; init; }
    public int? MaxTokens { get; init; }
    public int? TimeoutSecs { get; init; }
    public int? BashTimeoutSecs { get; init; }
    public int? MaxRetries { get; init; }
    public int? MaxContextTokens { get; init; }
    public int? SessionRetention { get; init; }
    public string? Workspace { get; init; }

    /// <summary>The <c>[theme]</c> table (ICARUS-108).</summary>
    public ThemePartial? Theme { get; init; }

    /// <summary>The <c>[theme]</c> preset name from a flag.</summary>
    public string? ThemeName { get; init; }

    /// <summary>
    /// Overlay <paramref name="higher"/> on top of this source: every non-null
    /// field in <paramref name="higher"/> wins.
    /// </summary>
    public PartialConfig Overlay(PartialConfig higher) => new()
    {
        Provider = higher.Provider ?? Provider,
        Model = higher.Model ?? Model,
        Region = higher.Region ?? Region,
        BaseUrl = higher.BaseUrl ?? BaseUrl,
        Temperature = higher.Temperature ?? Temperature,
        MaxIterations = higher.MaxIterations ?? MaxIterations,
        MaxOutputBytes = higher.MaxOutputBytes ?? MaxOutputBytes,
        MaxTokens = higher.MaxTokens ?? MaxTokens,
        TimeoutSecs = higher.TimeoutSecs ?? TimeoutSecs,
        BashTimeoutSecs = higher.BashTimeoutSecs ?? BashTimeoutSecs,
        MaxRetries = higher.MaxRetries ?? MaxRetries,
        MaxContextTokens = higher.MaxContextTokens ?? MaxContextTokens,
        SessionRetention = higher.SessionRetention ?? SessionRetention,
        Workspace = higher.Workspace ?? Workspace,
        Theme = higher.Theme ?? Theme,
        ThemeName = higher.ThemeName ?? ThemeName,
    };
}
