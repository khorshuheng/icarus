using Amazon;
using Amazon.BedrockRuntime;
using Anthropic;
using Microsoft.Extensions.AI;
using Icarus.Core.Credentials;
using Icarus.Core.Config;
using Icarus.Core.Provider;
using Icarus.Core.Providers.Meai;
using AgentConfig = Icarus.Core.Config.Config;
using ConfigException = Icarus.Core.Config.ConfigException;
using CredentialResolver = Icarus.Core.Credentials.Credentials;

namespace Icarus.Core.Providers;

/// <summary>
/// Builds the provider for a resolved configuration (ICARUS-102), using each
/// vendor's <b>official</b> client: the AWS SDK for Bedrock (default
/// credential chain, so compute credentials work) and Anthropic's own SDK.
/// No community wrapper, no hand-written HTTP/SSE.
/// </summary>
public static class ProviderBuilder
{
    /// <summary>Build the provider selected by <paramref name="config"/>.</summary>
    public static IProvider Build(AgentConfig config) => config.Provider.Name switch
    {
        "fake" => new FakeProvider([]),
        "bedrock" => BuildBedrock(config),
        "anthropic" => BuildAnthropic(config),
        _ => throw new ConfigException($"unknown provider '{config.Provider.Name}'"),
    };

    /// <summary>The credential resolver handed to the runtime (flag &gt; env &gt; keyring).</summary>
    public static string? ResolveKey(ProviderInfo provider) => CredentialResolver.Resolve(provider, null);

    private static IProvider BuildBedrock(AgentConfig config)
    {
        AmazonBedrockRuntimeClient runtime;
        try
        {
            runtime = string.IsNullOrEmpty(config.Region)
                ? new AmazonBedrockRuntimeClient()
                : new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(config.Region));
        }
        catch (Exception error)
        {
            throw new ConfigException(
                $"could not create the Bedrock client: {error.Message} "
                + "(set `region` in config.toml or AWS_REGION)");
        }

        return Wrap(runtime.AsIChatClient(config.Model), config);
    }

    private static IProvider BuildAnthropic(AgentConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ConfigException("no API key for provider 'anthropic'");
        }

        var client = new AnthropicClient
        {
            ApiKey = config.ApiKey,
            BaseUrl = config.EffectiveBaseUrl,
        };

        return Wrap(client.AsIChatClient(config.Model, config.MaxTokens), config);
    }

    private static IProvider Wrap(Microsoft.Extensions.AI.IChatClient client, AgentConfig config) =>
        new MeaiProvider(new MeaiProviderOptions
        {
            Client = client,
            EffortStyle = EffortCapability.Resolve(config.Provider, config.Model),
            MaxOutputTokens = config.MaxTokens,
            Temperature = config.Temperature,
            MaxRetries = config.MaxRetries,
        });
}
