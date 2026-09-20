using System.Text.Json.Nodes;
using System.Threading.Channels;
using Icarus.Core.Config;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;
using Icarus.Core.Serialization;
using Icarus.Core.Tools;
using Icarus.Core.Workspaces;
using AgentConfig = Icarus.Core.Config.Config;

namespace Icarus.Core.Tests;

public class RuntimeTests
{
    private static AgentConfig FakeConfig(TempWorkspace ws, int? maxContextTokens = null) => ConfigLoader.Resolve(
        ws.Dir,
        new PartialConfig { Provider = "fake", Model = "m", MaxContextTokens = maxContextTokens },
        new PartialConfig(),
        null);

    private static AgentRuntime NewRuntime(
        TempWorkspace ws,
        IProvider provider,
        ToolSet? tools = null,
        AgentConfig? config = null,
        ProviderFactory? factory = null,
        ApiKeyResolver? resolver = null) =>
        new(config ?? FakeConfig(ws), provider, tools ?? new ToolSet([new StubTool("read")]),
            ws.Workspace, factory, resolver);

    private static List<Event> Drain(AgentRuntime runtime)
    {
        var events = new List<Event>();
        while (runtime.Events.TryRead(out var @event))
        {
            events.Add(@event);
        }

        return events;
    }

    private static async Task<List<Event>> CollectAsync(
        ChannelReader<Event> reader,
        Func<Event, bool> until,
        int timeoutSeconds = 10)
    {
        var events = new List<Event>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            while (await reader.WaitToReadAsync(cts.Token))
            {
                while (reader.TryRead(out var @event))
                {
                    events.Add(@event);
                    if (until(@event))
                    {
                        return events;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // timed out; return what we have
        }

        return events;
    }

    // ---- serialization ---------------------------------------------------

    [Fact]
    public void Events_serialize_with_a_snake_case_type_discriminator()
    {
        Event @event = new TextDeltaEvent("hi");

        var json = IcarusJson.Serialize(@event);

        Assert.Contains("\"type\":\"text_delta\"", json);
        Assert.Contains("\"text\":\"hi\"", json);
        Assert.Equal(@event, IcarusJson.Deserialize<Event>(json));
    }

    [Fact]
    public void Commands_serialize_with_a_snake_case_type_discriminator()
    {
        Command command = new SetEffortCommand(Effort.High);

        var json = IcarusJson.Serialize(command);

        Assert.Contains("\"type\":\"set_effort\"", json);
        Assert.Contains("\"effort\":\"high\"", json);
        Assert.Equal(command, IcarusJson.Deserialize<Command>(json));
    }

    // ---- turns -----------------------------------------------------------

    [Fact]
    public async Task Runs_a_text_turn_and_records_the_conversation()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("hello"));

        var final = await runtime.RunOnceAsync("hi");

        Assert.Equal("hello", final);
        var events = Drain(runtime);
        Assert.Contains(events, e => e is TurnStartEvent);
        Assert.Contains(events, e => e is TextDeltaEvent { Text: "hello" });
        Assert.Contains(events, e => e is TurnEndEvent);
        Assert.Contains(events, e => e is AgentSettledEvent { Text: "hello", Interrupted: false });

        var history = runtime.History;
        Assert.IsType<Message.System>(history[0]);
        Assert.Contains(history, m => m is Message.User);
        Assert.Contains(history, m => m is Message.Assistant { Text: "hello" });
    }

    [Fact]
    public void The_system_prompt_describes_four_tools_and_no_search()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("x"));

        var prompt = runtime.SystemPrompt();

        Assert.Contains("exactly four tools", prompt);
        Assert.Contains("read, bash, edit, write", prompt);
        Assert.DoesNotContain("search", prompt);
        Assert.Contains("absolute paths and `..` are allowed", prompt);
    }

    [Fact]
    public async Task Runs_tool_calls_then_answers()
    {
        using var ws = new TempWorkspace();
        var stub = new StubTool("read");
        var provider = FakeProvider.Scripted(
            FakeProvider.ScriptedTurn.FromToolCalls(
                [new ToolCall("c1", "read", new JsonObject { ["path"] = "a.txt" })]),
            FakeProvider.ScriptedTurn.FromText("done"));
        var runtime = NewRuntime(ws, provider, new ToolSet([stub]));

        var final = await runtime.RunOnceAsync("read a.txt");

        Assert.Equal("done", final);
        Assert.Equal(1, stub.Invocations);
        var events = Drain(runtime);
        Assert.Contains(events, e => e is ToolStartEvent { Name: "read" });
        Assert.Contains(events, e => e is ToolEndEvent { Name: "read", Ok: true });
        Assert.Contains(runtime.History, m => m is Message.ToolResult { Result: "stub-output" });
    }

    [Fact]
    public async Task Truncated_tool_calls_are_never_executed()
    {
        using var ws = new TempWorkspace();
        var stub = new StubTool("read");
        var provider = FakeProvider.Scripted(
            new FakeProvider.ScriptedTurn([], new Response.TruncatedToolCalls(
                [new ToolCall("c1", "read", new JsonObject())])),
            FakeProvider.ScriptedTurn.FromText("recovered"));
        var runtime = NewRuntime(ws, provider, new ToolSet([stub]));

        var final = await runtime.RunOnceAsync("go");

        Assert.Equal("recovered", final);
        Assert.Equal(0, stub.Invocations);
        Assert.Contains(runtime.History,
            m => m is Message.ToolResult toolResult && toolResult.Result.Contains("was not executed"));
    }

    // ---- control ---------------------------------------------------------

    [Fact]
    public async Task Set_model_emits_state_changed()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("x"));
        _ = runtime.Start();

        runtime.SetModel("other");
        var events = await CollectAsync(runtime.Events, e => e is StateChangedEvent { Model: "other" });

        Assert.Equal("other", runtime.State.Model);
        Assert.Contains(events, e => e is StateChangedEvent { Model: "other" });
        runtime.Shutdown();
    }

    [Fact]
    public async Task Set_effort_emits_state_changed()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("x"));
        _ = runtime.Start();

        runtime.SetEffort(Effort.High);
        var events = await CollectAsync(runtime.Events, e => e is StateChangedEvent { Effort: Effort.High });

        Assert.Equal(Effort.High, runtime.State.Effort);
        Assert.Contains(events, e => e is StateChangedEvent { Effort: Effort.High });
        runtime.Shutdown();
    }

    [Fact]
    public async Task Clear_resets_the_conversation()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("x"));
        await runtime.RunOnceAsync("hi");
        Assert.True(runtime.History.Count > 1);

        _ = runtime.Start();
        runtime.Clear();
        await CollectAsync(runtime.Events, e => e is StateChangedEvent);

        Assert.Single(runtime.History);
        Assert.IsType<Message.System>(runtime.History[0]);
        runtime.Shutdown();
    }

    [Fact]
    public async Task Switching_provider_keeps_the_conversation()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(
            ws,
            FakeProvider.Text("x"),
            factory: _ => FakeProvider.Text("y"),
            resolver: _ => "key");
        await runtime.RunOnceAsync("hi");
        var before = runtime.History.Count;

        _ = runtime.Start();
        runtime.SetProvider("anthropic");
        await CollectAsync(runtime.Events, e => e is StateChangedEvent { Provider: "anthropic" });

        Assert.Equal("anthropic", runtime.ProviderInfo.Name);
        Assert.Equal(before, runtime.History.Count);
        runtime.Shutdown();
    }

    [Fact]
    public async Task Switching_to_a_keyless_provider_is_refused()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(
            ws,
            FakeProvider.Text("x"),
            factory: _ => FakeProvider.Text("y"),
            resolver: _ => null);
        _ = runtime.Start();

        runtime.SetProvider("anthropic");
        var events = await CollectAsync(runtime.Events, e => e is ErrorEvent);

        Assert.Equal("fake", runtime.ProviderInfo.Name);
        Assert.Contains(events, e => e is ErrorEvent { Message: var m } && m.Contains("no API key"));
        runtime.Shutdown();
    }

    [Fact]
    public async Task Unknown_provider_is_refused_with_the_supported_list()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, FakeProvider.Text("x"), factory: _ => FakeProvider.Text("y"));
        _ = runtime.Start();

        runtime.SetProvider("openai");
        var events = await CollectAsync(runtime.Events, e => e is ErrorEvent);

        Assert.Contains(events, e => e is ErrorEvent { Message: var m } && m.Contains("unknown provider 'openai'"));
        runtime.Shutdown();
    }

    [Fact]
    public async Task Abort_interrupts_an_in_flight_turn()
    {
        using var ws = new TempWorkspace();
        var provider = new BlockingProvider();
        var runtime = NewRuntime(ws, provider);
        _ = runtime.Start();

        runtime.Prompt("hi");
        await provider.Started.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Abort();

        var events = await CollectAsync(runtime.Events, e => e is AgentSettledEvent);

        Assert.Contains(events, e => e is AgentSettledEvent { Interrupted: true });
        Assert.False(runtime.IsBusy);
        runtime.Shutdown();
    }

    [Fact]
    public async Task Models_listing_is_reported()
    {
        using var ws = new TempWorkspace();
        var provider = new FakeProvider(
            [FakeProvider.ScriptedTurn.FromText("x")],
            [new[] { "m1", "m2" }]);
        var runtime = NewRuntime(ws, provider);
        _ = runtime.Start();

        runtime.ListModels();
        var events = await CollectAsync(runtime.Events, e => e is ModelsListedEvent);

        Assert.Contains(events, e => e is ModelsListedEvent { Models: var models } && models.SequenceEqual(new[] { "m1", "m2" }));
        runtime.Shutdown();
    }

    // ---- budgeting -------------------------------------------------------

    [Fact]
    public async Task History_is_trimmed_when_over_budget()
    {
        using var ws = new TempWorkspace();
        var runtime = NewRuntime(ws, new TokenReportingProvider(50_000), config: FakeConfig(ws, maxContextTokens: 100));

        for (var i = 0; i < 5; i++)
        {
            await runtime.RunOnceAsync($"message {i}");
        }

        Assert.True(runtime.History.Count <= 6, $"history grew to {runtime.History.Count}");
        Assert.IsType<Message.System>(runtime.History[0]);
    }

    // ---- steering & limits ----------------------------------------------

    [Fact]
    public async Task A_steer_is_delivered_at_the_next_boundary()
    {
        using var ws = new TempWorkspace();
        var provider = new GateProvider();
        var runtime = NewRuntime(ws, provider);
        _ = runtime.Start();

        runtime.Prompt("first");
        await provider.FirstStarted.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Steer("steer message");
        provider.Release();

        var events = await CollectAsync(runtime.Events, e => e is AgentSettledEvent);

        Assert.Contains(events, e => e is AgentSettledEvent { Text: "done" });
        IReadOnlyList<Message> second;
        lock (provider.Histories)
        {
            second = provider.Histories[1];
        }

        Assert.Contains(second, m => m is Message.User { Text: "steer message" });
        runtime.Shutdown();
    }

    [Fact]
    public async Task Thinking_deltas_are_streamed()
    {
        using var ws = new TempWorkspace();
        var turn = new FakeProvider.ScriptedTurn(
            [StreamDelta.Thinking("hmm"), StreamDelta.Content("answer")],
            new Response.Text("answer"));
        var runtime = NewRuntime(ws, FakeProvider.Scripted(turn));

        await runtime.RunOnceAsync("hi");

        var events = Drain(runtime);
        Assert.Contains(events, e => e is ThinkingDeltaEvent { Text: "hmm" });
        Assert.Contains(events, e => e is TextDeltaEvent { Text: "answer" });
    }

    [Fact]
    public async Task The_iteration_cap_ends_a_runaway_turn()
    {
        using var ws = new TempWorkspace();
        var config = ConfigLoader.Resolve(
            ws.Dir,
            new PartialConfig { Provider = "fake", Model = "m", MaxIterations = 1 },
            new PartialConfig(),
            null);
        var runtime = NewRuntime(ws, new AlwaysToolCallsProvider(), config: config);

        await Assert.ThrowsAsync<RuntimeException>(() => runtime.RunOnceAsync("go"));

        Assert.Contains(Drain(runtime),
            e => e is ErrorEvent { Message: var m } && m.Contains("iteration cap"));
    }

    // ---- test doubles ----------------------------------------------------

    private sealed class StubTool(string name) : ITool
    {
        public int Invocations;

        public string Name => name;

        public string Description => "stub tool";

        public JsonNode Schema => new JsonObject { ["type"] = "object" };

        public Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Invocations);
            return Task.FromResult(new ToolOutput("stub-output"));
        }
    }

    private sealed class BlockingProvider : IProvider
    {
        private readonly TaskCompletionSource _started = new();

        public Task Started => _started.Task;

        public async Task<Completion> CompleteAsync(
            IReadOnlyList<Message> history,
            IReadOnlyList<ToolDefinition> tools,
            JsonNode effortParams,
            CancellationToken cancellationToken,
            Action<StreamDelta> onDelta)
        {
            _started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new Completion(new Response.Text(string.Empty), null, false);
        }
    }

    private sealed class TokenReportingProvider(int promptTokens) : IProvider
    {
        public Task<Completion> CompleteAsync(
            IReadOnlyList<Message> history,
            IReadOnlyList<ToolDefinition> tools,
            JsonNode effortParams,
            CancellationToken cancellationToken,
            Action<StreamDelta> onDelta)
        {
            onDelta(StreamDelta.Content("a"));
            return Task.FromResult(new Completion(new Response.Text("a"), promptTokens, false));
        }
    }

    private sealed class AlwaysToolCallsProvider : IProvider
    {
        public Task<Completion> CompleteAsync(
            IReadOnlyList<Message> history,
            IReadOnlyList<ToolDefinition> tools,
            JsonNode effortParams,
            CancellationToken cancellationToken,
            Action<StreamDelta> onDelta) =>
            Task.FromResult(new Completion(
                new Response.ToolCalls([new ToolCall("c1", "read", new JsonObject())]), null, false));
    }

    /// <summary>Blocks on the first completion until released, so a steer can be queued mid-turn.</summary>
    private sealed class GateProvider : IProvider
    {
        private readonly TaskCompletionSource _firstStarted = new();
        private readonly TaskCompletionSource _release = new();
        private int _calls;

        public List<IReadOnlyList<Message>> Histories { get; } = [];

        public Task FirstStarted => _firstStarted.Task;

        public void Release() => _release.TrySetResult();

        public async Task<Completion> CompleteAsync(
            IReadOnlyList<Message> history,
            IReadOnlyList<ToolDefinition> tools,
            JsonNode effortParams,
            CancellationToken cancellationToken,
            Action<StreamDelta> onDelta)
        {
            lock (Histories)
            {
                Histories.Add(history);
            }

            if (Interlocked.Increment(ref _calls) == 1)
            {
                _firstStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                return new Completion(
                    new Response.ToolCalls([new ToolCall("c1", "read", new JsonObject())]), null, false);
            }

            return new Completion(new Response.Text("done"), null, false);
        }
    }
}
