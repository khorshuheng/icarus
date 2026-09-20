namespace Icarus.Core.Config;

/// <summary>
/// How a provider's canonical <see cref="Provider.Effort"/> level maps to wire
/// parameters. <c>None</c> means the provider gets no effort parameters
/// (capability honesty over guesswork).
/// </summary>
public enum EffortStyle
{
    /// <summary>No effort parameters.</summary>
    None,

    /// <summary>Anthropic <c>thinking</c> block with a token budget.</summary>
    AnthropicThinking,

    /// <summary>Bedrock additional model request fields (Anthropic-on-Bedrock).</summary>
    BedrockReasoning,
}

/// <summary>
/// One registry entry: the wire facts ICARUS knows about a provider. There are
/// deliberately <b>no model presets</b> — the model comes from config or
/// <c>--model</c>.
/// </summary>
/// <param name="Name">Registry name (lower-case).</param>
/// <param name="PresetBaseUrl">Default base URL (empty for region-based Bedrock).</param>
/// <param name="ApiKeyEnv">Provider-native environment variable, or <c>null</c>.</param>
/// <param name="Effort">How thinking effort maps for this provider.</param>
public sealed record ProviderInfo(
    string Name,
    string PresetBaseUrl,
    string? ApiKeyEnv,
    EffortStyle Effort)
{
    /// <summary>True when the provider needs an API key (Bedrock uses AWS credentials instead).</summary>
    public bool RequiresKey => ApiKeyEnv is not null;
}

/// <summary>The provider registry (ICARUS-100/102).</summary>
public static class Providers
{
    /// <summary>Amazon Bedrock, via the official AWS SDK and the AWS credential chain.</summary>
    public static readonly ProviderInfo Bedrock =
        new("bedrock", string.Empty, null, EffortStyle.BedrockReasoning);

    /// <summary>Anthropic, via the official Anthropic SDK.</summary>
    public static readonly ProviderInfo Anthropic =
        new("anthropic", "https://api.anthropic.com", "ANTHROPIC_API_KEY", EffortStyle.AnthropicThinking);

    /// <summary>The scripted offline provider.</summary>
    public static readonly ProviderInfo Fake =
        new("fake", string.Empty, null, EffortStyle.None);

    /// <summary>Every registered provider, in registry order.</summary>
    public static readonly IReadOnlyList<ProviderInfo> All = [Bedrock, Anthropic, Fake];

    /// <summary>The default provider (the first registry row).</summary>
    public static ProviderInfo Default => All[0];

    /// <summary>Look up a provider by name (case-insensitive), or <c>null</c>.</summary>
    public static ProviderInfo? ByName(string name)
    {
        var wanted = name.Trim().ToLowerInvariant();
        return All.FirstOrDefault(p => p.Name == wanted);
    }

    /// <summary>The supported names, for error messages.</summary>
    public static string SupportedNames => string.Join(", ", All.Select(p => p.Name));
}
