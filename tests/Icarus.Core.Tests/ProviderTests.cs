using System.Text.Json.Nodes;
using Icarus.Core.Config;
using Icarus.Core.Credentials;
using CredentialResolver = Icarus.Core.Credentials.Credentials;
using Icarus.Core.Provider;
using Icarus.Core.Providers;
using Icarus.Core.Providers.Meai;
using Icarus.Core.Runtime;
using Microsoft.Extensions.AI;
using AgentConfig = Icarus.Core.Config.Config;

namespace Icarus.Core.Tests;

public class EffortCapabilityTests
{
    [Theory]
    [InlineData("anthropic", "claude-3-7-sonnet-20250219", EffortStyle.AnthropicThinking)]
    [InlineData("anthropic", "claude-sonnet-4-20250514", EffortStyle.AnthropicThinking)]
    [InlineData("anthropic", "claude-3-5-sonnet-20241022", EffortStyle.None)]
    [InlineData("anthropic", "claude-3-haiku-20240307", EffortStyle.None)]
    [InlineData("anthropic", "some-future-model", EffortStyle.AnthropicThinking)] // unknown => assume supported
    [InlineData("bedrock", "anthropic.claude-3-7-sonnet-20250219-v1:0", EffortStyle.AnthropicThinking)]
    [InlineData("bedrock", "anthropic.claude-3-5-sonnet-20241022-v2:0", EffortStyle.None)]
    [InlineData("bedrock", "meta.llama3-70b-instruct-v1:0", EffortStyle.None)]
    [InlineData("bedrock", "amazon.titan-text-express-v1", EffortStyle.None)]
    [InlineData("fake", "anything", EffortStyle.None)]
    public void Resolves_effort_per_model(string providerName, string model, EffortStyle expected)
    {
        var provider = Icarus.Core.Config.Providers.ByName(providerName)!;

        Assert.Equal(expected, EffortCapability.Resolve(provider, model));
    }
}

public class MeaiProviderTests
{
    private static readonly IReadOnlyList<Message> History = [new Message.User("hi")];

    private static MeaiProvider Provider(
        FakeChatClient client,
        EffortStyle style = EffortStyle.None,
        int maxTokens = 2_048,
        float temperature = 0.7f) =>
        new(new MeaiProviderOptions
        {
            Client = client,
            EffortStyle = style,
            MaxOutputTokens = maxTokens,
            Temperature = temperature,
        });

    [Fact]
    public async Task Streams_text_and_returns_the_completion()
    {
        var client = FakeChatClient.Text("hello");
        var provider = Provider(client);
        var deltas = new List<StreamDelta>();

        var completion = await provider.CompleteAsync(
            History, [], new JsonObject(), CancellationToken.None, deltas.Add);

        Assert.Equal([StreamDelta.Content("hello")], deltas);
        Assert.Equal(new Response.Text("hello"), completion.Response);
    }

    [Fact]
    public async Task Streams_reasoning_as_thinking_deltas()
    {
        var client = FakeChatClient.Updates(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("hmm")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]));
        var provider = Provider(client, EffortStyle.AnthropicThinking);
        var deltas = new List<StreamDelta>();

        await provider.CompleteAsync(
            History, [], EffortParams.For(EffortStyle.AnthropicThinking, Effort.Medium),
            CancellationToken.None, deltas.Add);

        Assert.Contains(StreamDelta.Thinking("hmm"), deltas);
        Assert.Contains(StreamDelta.Content("answer"), deltas);
    }

    [Fact]
    public async Task Maps_tool_calls_and_results_both_ways()
    {
        var call = new ToolCall("c1", "read", new JsonObject { ["path"] = "a.txt" });
        var client = FakeChatClient.Updates(
            new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("c1", "read", new Dictionary<string, object?> { ["path"] = "a.txt" })]));
        var provider = Provider(client);

        var completion = await provider.CompleteAsync(
            [new Message.User("read"), Message.Assistant.OfToolCalls([call]), new Message.ToolResult("c1", "content")],
            [new ToolDefinition("read", "read a file", new JsonObject { ["type"] = "object" })],
            new JsonObject(),
            CancellationToken.None,
            _ => { });

        var toolCalls = Assert.IsType<Response.ToolCalls>(completion.Response);
        Assert.Equal("c1", Assert.Single(toolCalls.Calls).Id);

        var assistant = client.LastMessages.First(m => m.Role == ChatRole.Assistant);
        Assert.Contains(assistant.Contents, c => c is FunctionCallContent);
        var toolMessage = client.LastMessages.First(m => m.Role == ChatRole.Tool);
        Assert.Contains(toolMessage.Contents, c => c is FunctionResultContent);
        Assert.Single(client.LastOptions!.Tools!);
    }

    [Fact]
    public async Task Merges_incremental_tool_call_arguments_by_call_id()
    {
        var client = FakeChatClient.Updates(
            new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("c1", "read", new Dictionary<string, object?> { ["path"] = "a" })]),
            new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("c1", "read", new Dictionary<string, object?> { ["path"] = "a.txt" })]));
        var provider = Provider(client);

        var completion = await provider.CompleteAsync(
            History, [], new JsonObject(), CancellationToken.None, _ => { });

        var toolCalls = Assert.IsType<Response.ToolCalls>(completion.Response);
        var merged = Assert.Single(toolCalls.Calls);
        Assert.Equal("c1", merged.Id);
        Assert.Equal("a.txt", merged.Args["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reports_prompt_tokens_from_usage()
    {
        var client = FakeChatClient.Updates(
            new ChatResponseUpdate(ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 42 })]));
        var provider = Provider(client);

        var completion = await provider.CompleteAsync(
            History, [], new JsonObject(), CancellationToken.None, _ => { });

        Assert.Equal(42, completion.PromptTokens);
    }

    [Fact]
    public async Task Truncated_tool_calls_are_flagged()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("c1", "read", new Dictionary<string, object?>())])
        {
            FinishReason = ChatFinishReason.Length,
        };
        var provider = Provider(FakeChatClient.Updates(update));

        var completion = await provider.CompleteAsync(
            History, [], new JsonObject(), CancellationToken.None, _ => { });

        Assert.IsType<Response.TruncatedToolCalls>(completion.Response);
    }

    [Fact]
    public async Task Thinking_suppresses_temperature_and_raises_max_tokens_over_budget()
    {
        var client = FakeChatClient.Text("x");
        var provider = Provider(client, EffortStyle.AnthropicThinking, maxTokens: 2_048);

        await provider.CompleteAsync(
            History, [], EffortParams.For(EffortStyle.AnthropicThinking, Effort.Medium),
            CancellationToken.None, _ => { });

        var options = client.LastOptions!;
        Assert.NotNull(options.Reasoning);
        Assert.Null(options.Temperature);
        Assert.True(options.MaxOutputTokens > 8_192, $"max tokens was {options.MaxOutputTokens}");
    }

    [Fact]
    public async Task Without_effort_temperature_is_sent_and_no_reasoning()
    {
        var client = FakeChatClient.Text("x");
        var provider = Provider(client, EffortStyle.None);

        await provider.CompleteAsync(
            History, [], EffortParams.For(EffortStyle.None, Effort.Medium),
            CancellationToken.None, _ => { });

        var options = client.LastOptions!;
        Assert.Null(options.Reasoning);
        Assert.Equal(0.7f, options.Temperature);
    }

    [Fact]
    public async Task Model_listing_is_unsupported_by_default()
    {
        var provider = Provider(FakeChatClient.Text("x"));

        await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => provider.ListModelsAsync(CancellationToken.None));
    }
}

public class ProviderErrorsTests
{
    private sealed class StatusException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    [Fact]
    public void Maps_status_codes_to_typed_errors()
    {
        Assert.IsType<ProviderAuthException>(ProviderErrors.Classify(new StatusException(401, "nope")));
        Assert.IsType<ProviderTimeoutException>(ProviderErrors.Classify(new StatusException(429, "slow")));
        Assert.IsType<ProviderHttpException>(ProviderErrors.Classify(new StatusException(500, "boom")));
    }

    [Fact]
    public void Quota_errors_are_not_transient()
    {
        var quota = new StatusException(429, "insufficient_quota");

        Assert.IsType<ProviderHttpException>(ProviderErrors.Classify(quota));
        Assert.False(ProviderErrors.IsTransient(quota));
        Assert.True(ProviderErrors.IsTransient(new StatusException(429, "rate limited")));
    }
}

public class CredentialsTests
{
    private sealed class MemoryStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = [];

        public void Store(string provider, string key) => _values[provider] = key;

        public string? Get(string provider) => _values.GetValueOrDefault(provider);

        public bool Delete(string provider) => _values.Remove(provider);
    }

    [Fact]
    public void Flag_beats_env_beats_keyring()
    {
        var previous = CredentialResolver.Store;
        var store = new MemoryStore();
        store.Store("anthropic", "keyring-key");
        CredentialResolver.Store = store;
        var provider = Icarus.Core.Config.Providers.ByName("anthropic")!;

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-key");
            Assert.Equal("flag-key", CredentialResolver.Resolve(provider, "flag-key"));
            Assert.Equal("env-key", CredentialResolver.Resolve(provider, null));
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Assert.Equal("keyring-key", CredentialResolver.Resolve(provider, null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            CredentialResolver.Store = previous;
        }
    }

    [Fact]
    public void Keyless_providers_resolve_to_null()
    {
        var bedrock = Icarus.Core.Config.Providers.ByName("bedrock")!;

        Assert.Null(CredentialResolver.Resolve(bedrock, null));
    }
}

public class ProviderBuilderTests
{
    private static AgentConfig Config(string provider, string model, string? apiKey = null) =>
        new()
        {
            Provider = Icarus.Core.Config.Providers.ByName(provider)!,
            Model = model,
            Workspace = "/tmp",
            ApiKey = apiKey,
            Region = provider == "bedrock" ? "us-east-1" : null,
        };

    [Fact]
    public void Builds_the_fake_provider()
    {
        Assert.IsType<FakeProvider>(ProviderBuilder.Build(Config("fake", "m")));
    }

    [Fact]
    public void Builds_the_official_bedrock_and_anthropic_backends_offline()
    {
        // Constructing the official clients must not touch the network.
        Assert.IsType<MeaiProvider>(ProviderBuilder.Build(Config("bedrock", "anthropic.claude-sonnet-4-20250514-v1:0")));
        Assert.IsType<MeaiProvider>(ProviderBuilder.Build(Config("anthropic", "claude-sonnet-4-20250514", "test-key")));
    }

    [Fact]
    public void Anthropic_without_a_key_fails_fast()
    {
        Assert.Throws<ConfigException>(() => ProviderBuilder.Build(Config("anthropic", "claude-sonnet-4-20250514")));
    }
}
