using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Icarus.Core.Providers.Meai;

/// <summary>
/// A scripted <see cref="IChatClient"/> for offline adapter tests (ICARUS-102).
/// It records the messages and options it is given and replays queued updates.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<IReadOnlyList<ChatResponseUpdate>> _scripts = new();

    /// <summary>The messages of the most recent call.</summary>
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    /// <summary>The options of the most recent call.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public int Calls { get; private set; }

    /// <summary>A client that streams one text update.</summary>
    public static FakeChatClient Text(string text) =>
        new([new ChatResponseUpdate(ChatRole.Assistant, text)]);

    /// <summary>A client that replays the given updates in order.</summary>
    public static FakeChatClient Updates(params ChatResponseUpdate[] updates) => new(updates);

    public FakeChatClient() { }

    public FakeChatClient(IEnumerable<ChatResponseUpdate> script) => Enqueue(script);

    /// <summary>Queue another scripted response for a subsequent call.</summary>
    public FakeChatClient Enqueue(IEnumerable<ChatResponseUpdate> updates)
    {
        _scripts.Enqueue(updates.ToArray());
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Capture(messages, options);
        var updates = Next();
        return Task.FromResult(updates.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Capture(messages, options);
        foreach (var update in Next())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private void Capture(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        Calls++;
        LastMessages = messages.ToArray();
        LastOptions = options;
    }

    private IReadOnlyList<ChatResponseUpdate> Next() =>
        _scripts.Count > 0 ? _scripts.Dequeue() : [new ChatResponseUpdate(ChatRole.Assistant, "done")];
}
