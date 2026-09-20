using Icarus.Core.Theme;
using ThemeType = Icarus.Core.Theme.Theme;

namespace Icarus.Core.Config;

/// <summary>A configuration problem that should fail fast with a clear message.</summary>
public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// Fully-resolved configuration passed to the provider and the agent loop.
/// Precedence is <c>flags &gt; config file &gt; defaults</c> (ICARUS-105); there
/// is no settings environment layer, and an API key can never come from the
/// config file.
/// </summary>
public sealed record Config
{
    /// <summary>The selected provider registry row.</summary>
    public required ProviderInfo Provider { get; init; }

    /// <summary>The provider model id (required; there are no model presets).</summary>
    public required string Model { get; init; }

    /// <summary>The workspace root.</summary>
    public required string Workspace { get; init; }

    /// <summary>The resolved API key, or <c>null</c> (resolved by ICARUS-102).</summary>
    public string? ApiKey { get; init; }

    /// <summary>AWS region for Bedrock; <c>null</c> lets the AWS chain decide.</summary>
    public string? Region { get; init; }

    /// <summary>Optional base-URL override (Anthropic, or a Bedrock endpoint).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Sampling temperature.</summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Iteration cap for scripted/test runs (the interactive TUI disables it).</summary>
    public int MaxIterations { get; init; } = 30;

    /// <summary>Cap for any single tool result / file read, in bytes.</summary>
    public int MaxOutputBytes { get; init; } = 32_000;

    /// <summary>Maximum tokens requested per completion (Anthropic requires this).</summary>
    public int MaxTokens { get; init; } = 2_048;

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSecs { get; init; } = 60;

    /// <summary>Default <c>bash</c> timeout in seconds; <c>0</c> disables it.</summary>
    public int BashTimeoutSecs { get; init; } = 120;

    /// <summary>Retries for transient failures (timeouts, 429, 5xx).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Token budget for the conversation history.</summary>
    public int MaxContextTokens { get; init; } = 32_000 - 4_096;

    /// <summary>Sessions kept per workspace; <c>0</c> disables pruning.</summary>
    public int SessionRetention { get; init; } = 10;

    /// <summary>The resolved TUI theme (ICARUS-108).</summary>
    public ThemeType Theme { get; init; } = ThemeType.Dark;

    /// <summary>The effective base URL (override, else the provider preset).</summary>
    public string EffectiveBaseUrl => BaseUrl ?? Provider.PresetBaseUrl;

    /// <summary>The <c>bash</c> default timeout, or <c>null</c> when disabled.</summary>
    public int? BashDefaultTimeout => BashTimeoutSecs > 0 ? BashTimeoutSecs : null;
}
