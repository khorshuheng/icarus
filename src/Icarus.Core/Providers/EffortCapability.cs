using Icarus.Core.Config;

namespace Icarus.Core.Providers;

/// <summary>
/// Resolves reasoning/thinking capability **per model** (ICARUS-102). The
/// provider row only says whether the provider has a reasoning mechanism at
/// all; the model id decides whether it applies. Unknown models are assumed
/// supported (owner decision), so the table is an explicit deny-list of known
/// non-reasoning families.
/// </summary>
public static class EffortCapability
{
    private static readonly string[] NonReasoningClaudeFamilies =
    [
        "claude-2",
        "claude-instant",
        "claude-3-5",
        "claude-3-sonnet",
        "claude-3-haiku",
        "claude-3-opus",
    ];

    /// <summary>The effort style to apply for <paramref name="provider"/> + <paramref name="model"/>.</summary>
    public static EffortStyle Resolve(ProviderInfo provider, string model) => provider.Name switch
    {
        "anthropic" => Claude(model),
        "bedrock" => Bedrock(model),
        _ => provider.Effort,
    };

    private static EffortStyle Bedrock(string model)
    {
        var id = model.ToLowerInvariant();

        // Only Anthropic-on-Bedrock takes the Anthropic thinking fields;
        // non-Anthropic Bedrock models (Titan, Llama, Mistral, …) do not.
        return id.StartsWith("anthropic.", StringComparison.Ordinal)
            || id.Contains("anthropic.claude", StringComparison.Ordinal)
                ? Claude(id)
                : EffortStyle.None;
    }

    private static EffortStyle Claude(string model)
    {
        var id = model.ToLowerInvariant();
        return NonReasoningClaudeFamilies.Any(id.Contains)
            ? EffortStyle.None
            : EffortStyle.AnthropicThinking;
    }
}
