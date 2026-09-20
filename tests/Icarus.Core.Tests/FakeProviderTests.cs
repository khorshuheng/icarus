using System.Text.Json.Nodes;
using Icarus.Core.Provider;

namespace Icarus.Core.Tests;

public class FakeProviderTests
{
    private static readonly JsonNode EmptyEffort = new JsonObject();
    private static readonly IReadOnlyList<ToolDefinition> NoTools = [];

    [Fact]
    public async Task Streams_text_deltas_and_returns_the_completion()
    {
        var provider = FakeProvider.Text("hello");
        var deltas = new List<StreamDelta>();

        var completion = await provider.CompleteAsync(
            [new Message.User("hi")], NoTools, EmptyEffort, CancellationToken.None, deltas.Add);

        Assert.Equal(new[] { StreamDelta.Content("hello") }, deltas);
        Assert.Equal(new Response.Text("hello"), completion.Response);
        Assert.Null(completion.PromptTokens);
        Assert.False(completion.Aborted);
    }

    [Fact]
    public async Task Streams_thinking_deltas_for_reasoning()
    {
        var turn = new FakeProvider.ScriptedTurn(
            [StreamDelta.Thinking("hmm"), StreamDelta.Content("answer")],
            new Response.Text("answer"),
            PromptTokens: 7);
        var provider = FakeProvider.Scripted(turn);
        var deltas = new List<StreamDelta>();

        var completion = await provider.CompleteAsync(
            [new Message.User("hi")], NoTools, EmptyEffort, CancellationToken.None, deltas.Add);

        Assert.Equal(new[] { StreamDelta.Thinking("hmm"), StreamDelta.Content("answer") }, deltas);
        Assert.Equal(7, completion.PromptTokens);
    }

    [Fact]
    public async Task Records_every_history_it_is_given()
    {
        var provider = FakeProvider.Text("ok");
        var history = new List<Message> { new Message.System("seed"), new Message.User("hi") };

        await provider.CompleteAsync(history, NoTools, EmptyEffort, CancellationToken.None, _ => { });

        var recorded = Assert.Single(provider.RecordedHistories);
        Assert.Equal(history, recorded);
    }

    [Fact]
    public async Task Returns_tool_calls_without_streamed_text()
    {
        var call = new ToolCall("call-1", "read", new JsonObject { ["path"] = "a.txt" });
        var provider = FakeProvider.ToolCalls(call);

        var completion = await provider.CompleteAsync(
            [new Message.User("read a.txt")], NoTools, EmptyEffort, CancellationToken.None, _ => { });

        var toolCalls = Assert.IsType<Response.ToolCalls>(completion.Response);
        Assert.Equal(call, Assert.Single(toolCalls.Calls));
    }

    [Fact]
    public async Task Falls_back_to_a_done_answer_when_the_script_is_exhausted()
    {
        var provider = FakeProvider.Text("first");

        var first = await provider.CompleteAsync(
            [new Message.User("a")], NoTools, EmptyEffort, CancellationToken.None, _ => { });
        var second = await provider.CompleteAsync(
            [new Message.User("b")], NoTools, EmptyEffort, CancellationToken.None, _ => { });

        Assert.Equal(new Response.Text("first"), first.Response);
        Assert.Equal(new Response.Text("done"), second.Response);
    }

    [Fact]
    public async Task Model_listing_is_unsupported_by_default()
    {
        var provider = FakeProvider.Text("ok");

        await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => provider.ListModelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        var provider = FakeProvider.Text("ok");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(
            [new Message.User("hi")], NoTools, EmptyEffort, cts.Token, _ => { }));
    }
}
