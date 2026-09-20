using System.Text.Json.Nodes;

namespace Icarus.Core.Provider;

/// <summary>
/// A deterministic, scripted provider for offline tests. It records every
/// history it is given so tests can assert what the loop sent, and returns a
/// queue of scripted turns (defaulting to a plain "done" answer when the queue
/// is exhausted).
/// </summary>
public sealed class FakeProvider : IProvider
{
    private readonly Lock _gate = new();
    private readonly Queue<ScriptedTurn> _turns;
    private readonly Queue<IReadOnlyList<string>> _modelLists;

    public FakeProvider(
        IEnumerable<ScriptedTurn> turns,
        IEnumerable<IReadOnlyList<string>>? modelLists = null)
    {
        _turns = new Queue<ScriptedTurn>(turns);
        _modelLists = new Queue<IReadOnlyList<string>>(modelLists ?? []);
    }

    /// <summary>A scripted provider that answers with one text turn.</summary>
    public static FakeProvider Text(string text) => new([ScriptedTurn.FromText(text)]);

    /// <summary>A scripted provider that answers with one tool-call turn.</summary>
    public static FakeProvider ToolCalls(params ToolCall[] calls) =>
        new([ScriptedTurn.FromToolCalls(calls)]);

    /// <summary>A scripted provider with an explicit sequence of turns.</summary>
    public static FakeProvider Scripted(params ScriptedTurn[] turns) => new(turns);

    /// <summary>Every history passed to <see cref="CompleteAsync"/>, in order.</summary>
    public List<IReadOnlyList<Message>> RecordedHistories { get; } = [];

    /// <inheritdoc />
    public Task<Completion> CompleteAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<JsonNode> tools,
        JsonNode effortParams,
        CancellationToken cancellationToken,
        Action<StreamDelta> onDelta)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ScriptedTurn turn;
        lock (_gate)
        {
            RecordedHistories.Add(history);
            turn = _turns.Count > 0 ? _turns.Dequeue() : ScriptedTurn.FromText("done");
        }

        foreach (var delta in turn.Deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onDelta(delta);
        }

        return Task.FromResult(new Completion(turn.Response, turn.PromptTokens, turn.Aborted));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_modelLists.Count > 0)
            {
                return Task.FromResult(_modelLists.Dequeue());
            }
        }

        return Task.FromException<IReadOnlyList<string>>(
            new ProviderUnsupportedException("fake provider has no model list"));
    }

    /// <summary>One scripted turn: its streamed deltas, final response and usage.</summary>
    public sealed record ScriptedTurn(
        IReadOnlyList<StreamDelta> Deltas,
        Response Response,
        int? PromptTokens = null,
        bool Aborted = false)
    {
        /// <summary>A turn that streams <paramref name="text"/> and answers with it.</summary>
        public static ScriptedTurn FromText(string text, int? promptTokens = null, bool aborted = false) =>
            new([StreamDelta.Content(text)], new Response.Text(text), promptTokens, aborted);

        /// <summary>A turn that answers with tool calls (no streamed text).</summary>
        public static ScriptedTurn FromToolCalls(IReadOnlyList<ToolCall> calls, int? promptTokens = null) =>
            new([], new Response.ToolCalls(calls), promptTokens);
    }
}
