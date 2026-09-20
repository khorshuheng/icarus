using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Icarus.Core.Config;
using Icarus.Core.Provider;

namespace Icarus.Core.Providers.Meai;

/// <summary>Construction options for <see cref="MeaiProvider"/>.</summary>
public sealed class MeaiProviderOptions
{
    /// <summary>The underlying MEAI client (Bedrock, Anthropic, or a fake).</summary>
    public required Microsoft.Extensions.AI.IChatClient Client { get; init; }

    /// <summary>The per-model effort style resolved by <see cref="EffortCapability"/>.</summary>
    public required EffortStyle EffortStyle { get; init; }

    /// <summary>Maximum output tokens when reasoning is off.</summary>
    public int MaxOutputTokens { get; init; } = 2_048;

    /// <summary>Sampling temperature when reasoning is off.</summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Retries for transient failures before any output is streamed.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Optional model listing (Bedrock/Anthropic); null means unsupported.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? ModelLister { get; init; }
}

/// <summary>
/// The only module in the core that names <c>Microsoft.Extensions.AI</c>
/// (ICARUS-102). It maps ICARUS's canonical types to MEAI, applies thinking
/// effort per model, and classifies errors — while ICARUS keeps its own loop.
/// </summary>
public sealed class MeaiProvider(MeaiProviderOptions options) : IProvider
{
    /// <inheritdoc />
    public async Task<Completion> CompleteAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> tools,
        JsonNode effortParams,
        CancellationToken cancellationToken,
        Action<StreamDelta> onDelta)
    {
        var messages = MapMessages(history);
        var thinkingEnabled = IsThinkingEnabled(effortParams);
        var chatOptions = BuildOptions(tools, effortParams, thinkingEnabled);

        for (var attempt = 0; ; attempt++)
        {
            var emitted = false;
            try
            {
                return await StreamOnceAsync(
                    messages, chatOptions, cancellationToken, delta =>
                    {
                        emitted = true;
                        onDelta(delta);
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (!emitted
                && attempt < options.MaxRetries
                && ProviderErrors.IsTransient(error))
            {
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
            catch (Exception error) when (error is not ProviderException)
            {
                throw ProviderErrors.Classify(error);
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        options.ModelLister is null
            ? Task.FromException<IReadOnlyList<string>>(
                new ProviderUnsupportedException("provider does not support model listing"))
            : options.ModelLister(cancellationToken);

    private async Task<Completion> StreamOnceAsync(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions chatOptions,
        CancellationToken cancellationToken,
        Action<StreamDelta> onDelta)
    {
        var text = new StringBuilder();
        var callOrder = new List<string>();
        var calls = new Dictionary<string, ToolCall>(StringComparer.Ordinal);
        int? promptTokens = null;
        Microsoft.Extensions.AI.ChatFinishReason? finish = null;

        await foreach (var update in options.Client.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
        {
            if (update.FinishReason is { } reason)
            {
                finish = reason;
            }

            var emittedText = false;
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case Microsoft.Extensions.AI.TextContent { Text.Length: > 0 } chunk:
                        text.Append(chunk.Text);
                        onDelta(StreamDelta.Content(chunk.Text));
                        emittedText = true;
                        break;
                    case Microsoft.Extensions.AI.TextReasoningContent { Text.Length: > 0 } reasoning:
                        onDelta(StreamDelta.Thinking(reasoning.Text));
                        break;
                    case Microsoft.Extensions.AI.FunctionCallContent call:
                        MergeCall(callOrder, calls, call);
                        break;
                    case Microsoft.Extensions.AI.UsageContent usage:
                        promptTokens = (int?)usage.Details?.InputTokenCount ?? promptTokens;
                        break;
                }
            }

            if (!emittedText && update.Text is { Length: > 0 } loose)
            {
                text.Append(loose);
                onDelta(StreamDelta.Content(loose));
            }
        }

        var merged = callOrder.Select(id => calls[id]).ToList();
        Response response = merged.Count switch
        {
            0 => new Response.Text(text.ToString()),
            _ when finish == Microsoft.Extensions.AI.ChatFinishReason.Length => new Response.TruncatedToolCalls(merged),
            _ => new Response.ToolCalls(merged),
        };

        return new Completion(response, promptTokens, Aborted: false);
    }

    /// <summary>
    /// Merge a (possibly incremental) function call by call id: providers may
    /// stream a call's arguments across several updates, so later updates with
    /// non-empty arguments replace earlier partial ones rather than adding
    /// duplicate calls.
    /// </summary>
    private static void MergeCall(
        List<string> order,
        Dictionary<string, ToolCall> calls,
        Microsoft.Extensions.AI.FunctionCallContent call)
    {
        var id = string.IsNullOrEmpty(call.CallId) ? $"call-{order.Count}" : call.CallId;
        if (!calls.ContainsKey(id))
        {
            order.Add(id);
            calls[id] = new ToolCall(id, call.Name, ToJson(call.Arguments));
            return;
        }

        if (call.Arguments is { Count: > 0 })
        {
            calls[id] = calls[id] with { Name = call.Name, Args = ToJson(call.Arguments) };
        }
    }

    private Microsoft.Extensions.AI.ChatOptions BuildOptions(
        IReadOnlyList<ToolDefinition> tools,
        JsonNode effortParams,
        bool thinkingEnabled)
    {
        var chatOptions = new Microsoft.Extensions.AI.ChatOptions
        {
            Tools = tools.Select(ToDeclaration).Cast<Microsoft.Extensions.AI.AITool>().ToList(),
        };

        var budget = thinkingEnabled ? Budget(effortParams) : 0;
        if (thinkingEnabled)
        {
            // Anthropic extended thinking requires budget_tokens < max_tokens,
            // and is incompatible with temperature/top_p/top_k.
            chatOptions.Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
            {
                Effort = MapEffort(budget),
            };
            chatOptions.MaxOutputTokens = Math.Max(options.MaxOutputTokens, budget + 1_024);
        }
        else
        {
            chatOptions.Temperature = options.Temperature;
            chatOptions.MaxOutputTokens = options.MaxOutputTokens;
        }

        return chatOptions;
    }

    private static Microsoft.Extensions.AI.AITool ToDeclaration(ToolDefinition tool) =>
        Microsoft.Extensions.AI.AIFunctionFactory.CreateDeclaration(
            tool.Name,
            tool.Description,
            JsonSerializer.SerializeToElement(tool.Schema),
            returnJsonSchema: null);

    private bool IsThinkingEnabled(JsonNode effortParams) =>
        options.EffortStyle is EffortStyle.AnthropicThinking or EffortStyle.BedrockReasoning
        && effortParams["thinking"] is JsonObject thinking
        && thinking["type"]?.GetValue<string>() == "enabled";

    private static int Budget(JsonNode effortParams) =>
        effortParams["thinking"]?["budget_tokens"]?.GetValue<int>() ?? 8_192;

    private static Microsoft.Extensions.AI.ReasoningEffort MapEffort(int budget) => budget switch
    {
        <= 1_024 => Microsoft.Extensions.AI.ReasoningEffort.Low,
        <= 8_192 => Microsoft.Extensions.AI.ReasoningEffort.Medium,
        _ => Microsoft.Extensions.AI.ReasoningEffort.High,
    };

    private static List<Microsoft.Extensions.AI.ChatMessage> MapMessages(IReadOnlyList<Message> history)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>(history.Count);
        foreach (var message in history)
        {
            switch (message)
            {
                case Message.System system:
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.System, system.Text));
                    break;
                case Message.User user:
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.User, user.Text));
                    break;
                case Message.Assistant assistant:
                    messages.Add(MapAssistant(assistant));
                    break;
                case Message.ToolResult result:
                    messages.Add(new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Tool,
                        [new Microsoft.Extensions.AI.FunctionResultContent(result.ToolCallId, result.Result)]));
                    break;
            }
        }

        return messages;
    }

    private static Microsoft.Extensions.AI.ChatMessage MapAssistant(Message.Assistant assistant)
    {
        var contents = new List<Microsoft.Extensions.AI.AIContent>();
        if (assistant.Text is { Length: > 0 } text)
        {
            contents.Add(new Microsoft.Extensions.AI.TextContent(text));
        }

        foreach (var call in assistant.ToolCalls)
        {
            contents.Add(new Microsoft.Extensions.AI.FunctionCallContent(
                call.Id, call.Name, ArgumentDictionary(call.Args)));
        }

        return new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, contents);
    }

    private static IDictionary<string, object?> ArgumentDictionary(JsonNode args) =>
        args is JsonObject obj
            ? obj.ToDictionary(pair => pair.Key, pair => (object?)pair.Value?.DeepClone())
            : new Dictionary<string, object?>();

    private static JsonNode ToJson(IDictionary<string, object?>? arguments) =>
        arguments is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(arguments) as JsonObject ?? new JsonObject();

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
}
