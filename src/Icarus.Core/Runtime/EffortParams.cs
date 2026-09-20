using System.Text.Json.Nodes;
using Icarus.Core.Config;
using Icarus.Core.Provider;

namespace Icarus.Core.Runtime;

/// <summary>
/// Maps a canonical <see cref="Effort"/> to the provider-flavoured wire
/// parameters handed to <see cref="IProvider.CompleteAsync"/> (ICARUS-103). An
/// empty object means "nothing to add" (capability honesty).
/// </summary>
public static class EffortParams
{
    public static JsonNode For(ProviderInfo provider, Effort effort) => provider.Effort switch
    {
        EffortStyle.AnthropicThinking or EffortStyle.BedrockReasoning => Thinking(effort),
        _ => new JsonObject(),
    };

    private static JsonNode Thinking(Effort effort) => effort == Effort.Off
        ? new JsonObject { ["thinking"] = new JsonObject { ["type"] = "disabled" } }
        : new JsonObject
        {
            ["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = Budget(effort),
            },
        };

    /// <summary>The hardcoded thinking budget per level (CRAB-116 capability table).</summary>
    public static int Budget(Effort effort) => effort switch
    {
        Effort.Off => 0,
        Effort.Minimal => 1_024,
        Effort.Low => 4_096,
        Effort.Medium => 8_192,
        Effort.High => 16_384,
        _ => 8_192,
    };
}
