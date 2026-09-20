using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Icarus.Core.Config;
using Icarus.Core.Provider;
using Icarus.Core.Providers;
using ProviderRegistry = Icarus.Core.Config.Providers;
using Icarus.Core.Tools;
using Icarus.Core.Workspaces;
using AgentConfig = Icarus.Core.Config.Config;

namespace Icarus.Core.Runtime;

/// <summary>Builds a provider instance from a resolved configuration (ICARUS-102/103).</summary>
public delegate IProvider ProviderFactory(AgentConfig config);

/// <summary>Resolves the API key for a provider (flag &gt; env &gt; keyring; ICARUS-102).</summary>
public delegate string? ApiKeyResolver(ProviderInfo provider);

/// <summary>
/// The stateful, UI-agnostic agent runtime (ICARUS-103). A single worker loop
/// consumes <see cref="Command"/>s from a channel and emits <see cref="Event"/>s
/// onto another; frontends never own the loop.
/// </summary>
public sealed class AgentRuntime
{
    private readonly AgentConfig _config;
    private readonly ToolSet _tools;
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>();
    private readonly Channel<Event> _events = Channel.CreateUnbounded<Event>();
    private readonly Lock _gate = new();
    private readonly Lock _providerGate = new();
    private readonly ProviderFactory? _providerFactory;
    private readonly ApiKeyResolver? _apiKeyResolver;

    private List<Message> _history = [];
    private RuntimeState _state;
    private Workspace _workspace;
    private IProvider _provider;
    private ProviderInfo _providerInfo;
    private CancellationTokenSource? _turnCts;
    private IReadOnlyList<string> _models = [];
    private int _anchorTokens;
    private int _anchorLen;
    private bool _interactive;
    private Task? _worker;

    public AgentRuntime(
        AgentConfig config,
        IProvider provider,
        ToolSet tools,
        Workspace workspace,
        ProviderFactory? providerFactory = null,
        ApiKeyResolver? apiKeyResolver = null)
    {
        _config = config;
        _provider = provider;
        _providerInfo = config.Provider;
        _tools = tools;
        _workspace = workspace;
        _state = new RuntimeState(config.Model, config.Provider.Name, Effort.Medium, workspace.Root, false);
        _providerFactory = providerFactory;
        _apiKeyResolver = apiKeyResolver;
    }

    /// <summary>The event stream consumed by frontends.</summary>
    public ChannelReader<Event> Events => _events.Reader;

    /// <summary>The current workspace.</summary>
    public Workspace Workspace => _workspace;

    /// <summary>The canonical workspace root.</summary>
    public string WorkspaceRoot => _workspace.Root;

    /// <summary>The active provider.</summary>
    public IProvider Provider => _provider;

    /// <summary>The active provider's registry row.</summary>
    public ProviderInfo ProviderInfo => _providerInfo;

    /// <summary>The last discovered model list (empty until listing succeeds).</summary>
    public IReadOnlyList<string> Models => _models;

    /// <summary>A snapshot of the runtime's mutable state.</summary>
    public RuntimeState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Whether a turn is in flight.</summary>
    public bool IsBusy => State.Busy;

    /// <summary>A copy of the conversation history.</summary>
    public IReadOnlyList<Message> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    /// <summary><c>(name, description)</c> for the registered tools.</summary>
    public IReadOnlyList<(string Name, string Description)> ToolListing => _tools.Listing;

    /// <summary>Discovered skills (populated by ICARUS-109).</summary>
    public IReadOnlyList<RuntimeSkill> Skills { get; set; } = [];

    /// <summary>An optional transform applied to the base system prompt (skills catalog).</summary>
    public Func<string, string>? SystemPromptDecorator { get; set; }

    /// <summary>The workspace's current cancel token, when a turn is in flight.</summary>
    public CancellationToken CancelToken
    {
        get
        {
            lock (_gate)
            {
                return _turnCts?.Token ?? CancellationToken.None;
            }
        }
    }

    // ---- command surface -------------------------------------------------

    public void Prompt(string text) => _commands.Writer.TryWrite(new PromptCommand(text));

    public void Steer(string text) => _commands.Writer.TryWrite(new SteerCommand(text));

    public void FollowUp(string text) => _commands.Writer.TryWrite(new FollowUpCommand(text));

    /// <summary>Cancel the in-flight turn immediately, keeping partial text.</summary>
    public void Abort()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _turnCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the turn already finished
        }

        _commands.Writer.TryWrite(new AbortCommand());
    }

    public void SetModel(string model) => _commands.Writer.TryWrite(new SetModelCommand(model));

    public void SetProvider(string provider) => _commands.Writer.TryWrite(new SetProviderCommand(provider));

    public void SetEffort(Effort effort) => _commands.Writer.TryWrite(new SetEffortCommand(effort));

    public void SwitchWorkspace(string path) => _commands.Writer.TryWrite(new SwitchWorkspaceCommand(path));

    public void Clear() => _commands.Writer.TryWrite(new ClearCommand());

    public void Resume() => _commands.Writer.TryWrite(new ResumeCommand());

    public void GetState() => _commands.Writer.TryWrite(new GetStateCommand());

    public void ListModels() => _commands.Writer.TryWrite(new ListModelsCommand());

    /// <summary>Stop the worker loop once the current command completes.</summary>
    public void Shutdown() => _commands.Writer.TryComplete();

    /// <summary>Whether the interactive frontend is present (disables the iteration cap).</summary>
    public void SetInteractive(bool on) => _interactive = on;

    /// <summary>Start the worker loop on a background task.</summary>
    public Task Start()
    {
        _worker ??= Task.Run(async () =>
        {
            Emit(new AgentStartEvent(State.Model, State.Effort, _workspace.Root));
            await RunForeverAsync();
        });
        return _worker;
    }

    /// <summary>The worker loop: consume commands forever.</summary>
    public async Task RunForeverAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            await HandleIdleAsync(command);
        }
    }

    /// <summary>Run a single prompt to completion without a worker loop (tests/headless).</summary>
    public async Task<string> RunOnceAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Emit(new AgentStartEvent(State.Model, State.Effort, _workspace.Root));
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _turnCts = cts;
        }

        SetBusy(true);
        var finalText = string.Empty;
        var interrupted = false;
        try
        {
            (finalText, interrupted) = await DriveCoreAsync(prompt, cts);
        }
        finally
        {
            Emit(new AgentSettledEvent(finalText, interrupted));
            lock (_gate)
            {
                _turnCts = null;
            }

            cts.Dispose();
            SetBusy(false);
        }

        if (interrupted && finalText.Length == 0)
        {
            throw new RuntimeException("interrupted");
        }

        return finalText;
    }

    /// <summary>Replace the running history (used by the session store's resume).</summary>
    public void ReplaceHistory(IReadOnlyList<Message> history)
    {
        lock (_gate)
        {
            _history = new List<Message>(history);
            _anchorTokens = 0;
            _anchorLen = 0;
        }
    }

    /// <summary>Reset the conversation to the seed system prompt.</summary>
    public void ResetSync() => ResetToSeed();

    /// <summary>Ask the provider for its model list.</summary>
    public void RefreshModels() => ListModels();

    /// <summary>The base system prompt for four tools, plus any decorator output.</summary>
    public string SystemPrompt()
    {
        var prompt = $"""
            You are icarus, a minimal coding agent. You inspect and modify files in the workspace '{_workspace.Root}' by calling tools.
            You have exactly four tools and no others: read, bash, edit, write.
            - read: read one or more files (a path, a list of paths, or a directory + glob) with an optional line range; use offset to page through long files.
            - bash: run a shell command in the workspace; check results before trusting them.
            - edit: apply precise text replacements; each oldText must match exactly once.
            - write: create or overwrite a file.
            Rules:
            - Paths are relative to the workspace by default; absolute paths and `..` are allowed.
            - Read before editing; verify changes with bash.
            - Prefer the `read` tool over `cat`/`head`; use bash for commands, not for dumping files.
            - Make the smallest change that satisfies the request.
            - When finished, give a concise final answer.
            """;

        return SystemPromptDecorator is null ? prompt : SystemPromptDecorator(prompt);
    }

    // ---- worker ----------------------------------------------------------

    private async Task HandleIdleAsync(Command command)
    {
        switch (command)
        {
            case PromptCommand prompt:
                await DriveUntilSettledAsync(prompt.Text);
                break;
            case SteerCommand steer:
                await DriveUntilSettledAsync(steer.Text); // an idle steer is just a turn
                break;
            case FollowUpCommand followUp:
                await DriveUntilSettledAsync(followUp.Text);
                break;
            default:
                await ApplyControlAsync(command, [], []);
                break;
        }
    }

    private async Task DriveUntilSettledAsync(string first)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _turnCts = cts;
        }

        SetBusy(true);
        var finalText = string.Empty;
        var interrupted = false;
        try
        {
            (finalText, interrupted) = await DriveCoreAsync(first, cts);
        }
        finally
        {
            Emit(new AgentSettledEvent(finalText, interrupted));
            lock (_gate)
            {
                _turnCts = null;
            }

            cts.Dispose();
            SetBusy(false);
        }
    }

    private async Task<(string Text, bool Interrupted)> DriveCoreAsync(string first, CancellationTokenSource cts)
    {
        var saved = new List<string>();
        var (finalText, interrupted) = await RunOneUserMessageAsync(first, saved, cts);

        while (!interrupted)
        {
            var (steers, followUps) = await DrainQueueAsync();
            saved.AddRange(followUps);
            saved.AddRange(steers);
            if (saved.Count == 0)
            {
                break;
            }

            var next = saved[0];
            saved.RemoveAt(0);
            (finalText, interrupted) = await RunOneUserMessageAsync(next, saved, cts);
        }

        return (finalText, interrupted);
    }

    private async Task<(string Text, bool Interrupted)> RunOneUserMessageAsync(
        string userText,
        List<string> saved,
        CancellationTokenSource turnCts)
    {
        Emit(new TurnStartEvent());
        PushUser(userText);

        const int seedLength = 2;
        var iterations = 0;
        var finalText = string.Empty;
        var interrupted = false;
        var streamed = new StringBuilder();

        while (true)
        {
            var (steers, followUps) = await DrainQueueAsync();
            saved.AddRange(followUps);
            if (steers.Count > 0)
            {
                foreach (var steer in steers)
                {
                    PushUser(steer);
                }

                iterations = 0; // fresh budget for the steered message
            }

            if (turnCts.IsCancellationRequested)
            {
                interrupted = true;
                break;
            }

            if (!_interactive && iterations >= _config.MaxIterations)
            {
                Emit(new ErrorEvent(
                    $"iteration cap exceeded: no final answer after {_config.MaxIterations} iterations"));
                interrupted = true;
                break;
            }

            var history = SnapshotHistory();
            await ManageContextAsync(history, seedLength);
            ReplaceHistoryInternal(history);

            streamed.Clear();
            Completion completion;
            try
            {
                var effort = EffortParams.For(
                    EffortCapability.Resolve(_providerInfo, State.Model), State.Effort);
                completion = await _provider.CompleteAsync(history, _tools.Definitions, effort, turnCts.Token, delta =>
                {
                    if (delta.IsThinking)
                    {
                        Emit(new ThinkingDeltaEvent(delta.Text));
                    }
                    else
                    {
                        streamed.Append(delta.Text);
                        Emit(new TextDeltaEvent(delta.Text));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                if (streamed.Length > 0)
                {
                    finalText = streamed.ToString();
                    AppendAssistantText(finalText);
                }

                interrupted = true;
                break;
            }
            catch (ProviderException e)
            {
                Emit(new ErrorEvent(DescribeProviderError(e)));
                interrupted = true;
                break;
            }

            if (completion.PromptTokens is { } tokens)
            {
                Emit(new UsageEvent(tokens));
                lock (_gate)
                {
                    _anchorTokens = tokens;
                    _anchorLen = history.Count;
                }
            }

            if (completion.Aborted)
            {
                if (completion.Response is Response.Text partialText && partialText.Value.Length > 0)
                {
                    finalText = partialText.Value;
                    AppendAssistantText(finalText);
                }

                interrupted = true;
                break;
            }

            switch (completion.Response)
            {
                case Response.Text text:
                    AppendAssistantText(text.Value);
                    finalText = text.Value;
                    goto complete;
                case Response.ToolCalls calls:
                    AppendAssistantToolCalls(calls.Calls);
                    if (await RunToolsAsync(calls.Calls, _workspace, turnCts))
                    {
                        interrupted = true;
                        goto complete;
                    }

                    iterations++;
                    continue;
                case Response.TruncatedToolCalls truncated:
                    AppendAssistantToolCalls(truncated.Calls);
                    AppendTruncatedResults(truncated.Calls);
                    iterations++;
                    continue;
            }
        }

    complete:
        Emit(new TurnEndEvent());
        return (finalText, interrupted);
    }

    private async Task<(List<string> Steers, List<string> FollowUps)> DrainQueueAsync()
    {
        var steers = new List<string>();
        var followUps = new List<string>();
        while (_commands.Reader.TryRead(out var command))
        {
            await ApplyControlAsync(command, steers, followUps);
        }

        return (steers, followUps);
    }

    private async Task ApplyControlAsync(Command command, List<string> steers, List<string> followUps)
    {
        switch (command)
        {
            case SteerCommand steer:
                steers.Add(steer.Text);
                Emit(new QueueUpdateEvent(steers.Count + followUps.Count, "steer"));
                break;
            case FollowUpCommand followUp:
                followUps.Add(followUp.Text);
                Emit(new QueueUpdateEvent(steers.Count + followUps.Count, "follow_up"));
                break;
            case PromptCommand prompt:
                followUps.Add(prompt.Text); // a prompt received while busy is a follow-up
                break;
            case AbortCommand:
                break; // Abort() already cancelled the token
            case GetStateCommand:
                EmitStateChanged();
                break;
            case ClearCommand:
                ResetToSeed();
                EmitStateChanged();
                break;
            case ResumeCommand:
                break; // handled by the frontend's session store
            case ListModelsCommand:
                await ListModelsAsync();
                break;
            case SetProviderCommand setProvider:
                await SwitchProviderAsync(setProvider.Provider);
                break;
            case SetModelCommand setModel:
                SetState(State with { Model = setModel.Model });
                break;
            case SetEffortCommand setEffort:
                SetState(State with { Effort = setEffort.Effort });
                break;
            case SwitchWorkspaceCommand switchWorkspace:
                ApplySwitchWorkspace(switchWorkspace.Path);
                break;
        }
    }

    private async Task<bool> RunToolsAsync(
        IReadOnlyList<ToolCall> calls,
        Workspace workspace,
        CancellationTokenSource turnCts)
    {
        var results = await Task.WhenAll(calls.Select(call => RunToolAsync(call, workspace, turnCts.Token)));

        var cancelled = false;
        foreach (var result in results)
        {
            Emit(new ToolEndEvent(result.Call.Name, result.Error is null && !result.Cancelled, result.Error));
            AppendToolResult(result.Call.Id, result.Content);
            cancelled |= result.Cancelled;
        }

        return cancelled;
    }

    private async Task<ToolRunResult> RunToolAsync(ToolCall call, Workspace workspace, CancellationToken token)
    {
        Emit(new ToolStartEvent(call.Name, call.Id, call.Args));
        try
        {
            var output = await _tools.ExecuteAsync(workspace, call.Name, call.Args, token);
            return new ToolRunResult(call, output.Content, false, null);
        }
        catch (ToolCancelledException)
        {
            return new ToolRunResult(call, "tool error: cancelled", true, "cancelled");
        }
        catch (ToolException e)
        {
            return new ToolRunResult(call, $"tool error: {e.Message}", false, e.Message);
        }
    }

    private void AppendTruncatedResults(IReadOnlyList<ToolCall> calls)
    {
        foreach (var call in calls)
        {
            AppendToolResult(call.Id,
                $"Tool call \"{call.Name}\" was not executed: the response was truncated by the output "
                + "token limit, so its arguments may be incomplete. Re-issue the tool call with complete arguments.");
        }
    }

    private async Task ListModelsAsync()
    {
        try
        {
            _models = await _provider.ListModelsAsync(CancellationToken.None);
            Emit(new ModelsListedEvent(_models));
        }
        catch (ProviderUnsupportedException)
        {
            Emit(new ErrorEvent("provider does not support model listing"));
        }
        catch (Exception e)
        {
            Emit(new ErrorEvent($"could not list models: {e.Message}"));
        }
    }

    private async Task SwitchProviderAsync(string name)
    {
        var info = ProviderRegistry.ByName(name);
        if (info is null)
        {
            Emit(new ErrorEvent($"unknown provider '{name}' (supported: {ProviderRegistry.SupportedNames})"));
            return;
        }

        string? key = null;
        if (info.RequiresKey)
        {
            key = _apiKeyResolver?.Invoke(info);
            if (string.IsNullOrEmpty(key))
            {
                Emit(new ErrorEvent(ProviderBuilder.MissingKey(info)));
                return;
            }
        }

        if (_providerFactory is null)
        {
            Emit(new ErrorEvent("provider switching is not available in this build"));
            return;
        }

        IProvider built;
        try
        {
            built = _providerFactory(_config with
            {
                Provider = info,
                ApiKey = key,
                BaseUrl = null,
                Model = State.Model,
            });
        }
        catch (Exception e)
        {
            Emit(new ErrorEvent($"could not build provider '{info.Name}': {e.Message}"));
            return;
        }

        lock (_providerGate)
        {
            _provider = built;
            _providerInfo = info;
        }

        SetState(State with { Provider = info.Name });
        await ListModelsAsync();
    }

    private void ApplySwitchWorkspace(string path)
    {
        try
        {
            _workspace = Workspace.New(_workspace.Resolve(path));
        }
        catch (Exception e)
        {
            Emit(new ErrorEvent($"cannot switch workspace to '{path}': {e.Message}"));
            return;
        }

        SetState(State with { Workspace = _workspace.Root });
        ResetToSeed();
    }

    // ---- history & prompt ------------------------------------------------

    private void PushUser(string text)
    {
        lock (_gate)
        {
            var seed = SystemPrompt();
            if (_history.Count == 0)
            {
                _history.Add(new Message.System(seed));
            }
            else if (_history[0] is Message.System)
            {
                _history[0] = new Message.System(seed);
            }

            _history.Add(new Message.User(text));
        }
    }

    private void ResetToSeed()
    {
        lock (_gate)
        {
            _history = [new Message.System(SystemPrompt())];
            _anchorTokens = 0;
            _anchorLen = 0;
        }
    }

    private void AppendAssistantText(string text)
    {
        lock (_gate)
        {
            _history.Add(Message.Assistant.OfText(text));
        }
    }

    private void AppendAssistantToolCalls(IReadOnlyList<ToolCall> calls)
    {
        lock (_gate)
        {
            _history.Add(Message.Assistant.OfToolCalls(calls));
        }
    }

    private void AppendToolResult(string toolCallId, string result)
    {
        lock (_gate)
        {
            _history.Add(new Message.ToolResult(toolCallId, result));
        }
    }

    private List<Message> SnapshotHistory()
    {
        lock (_gate)
        {
            return new List<Message>(_history);
        }
    }

    private void ReplaceHistoryInternal(List<Message> history)
    {
        lock (_gate)
        {
            _history = history;
        }
    }

    private void SetState(RuntimeState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        EmitStateChanged();
    }

    private void SetBusy(bool busy)
    {
        lock (_gate)
        {
            _state = _state with { Busy = busy };
        }
    }

    private void EmitStateChanged()
    {
        var state = State;
        Emit(new StateChangedEvent(state.Model, state.Provider, state.Effort, state.Workspace));
    }

    private void Emit(Event @event) => _events.Writer.TryWrite(@event);

    private string DescribeProviderError(ProviderException error) => error is ProviderAuthException
        ? $"authentication failed for provider '{_providerInfo.Name}': check the API key "
          + $"({_providerInfo.ApiKeyEnv ?? "--api-key"}, or /login) — {error.Message}"
        : $"provider error: {error.Message}";

    // ---- context budgeting ----------------------------------------------

    private async Task ManageContextAsync(List<Message> history, int seedLength)
    {
        var budget = _config.MaxContextTokens;
        if (AnchoredTotal(history) > budget)
        {
            await CompactAsync(history, seedLength);
        }

        Trim(history, seedLength, budget);
    }

    private int AnchoredTotal(List<Message> history)
    {
        int anchorTokens, anchorLength;
        lock (_gate)
        {
            anchorTokens = _anchorTokens;
            anchorLength = _anchorLen;
        }

        var total = anchorTokens;
        for (var i = Math.Min(anchorLength, history.Count); i < history.Count; i++)
        {
            total += EstimateMessageTokens(history[i]);
        }

        return total;
    }

    private void Trim(List<Message> history, int seedLength, int budget)
    {
        while (history.Count > 1 && AnchoredTotal(history) > budget)
        {
            if (!RemoveOldestBlock(history, seedLength))
            {
                break;
            }
        }
    }

    private async Task CompactAsync(List<Message> history, int seedLength)
    {
        var guard = 0;
        while (AnchoredTotal(history) > _config.MaxContextTokens && guard++ < 16)
        {
            var start = FirstRemovable(history, seedLength);
            if (start < 0)
            {
                return;
            }

            var end = BlockEnd(history, start);
            var block = history.GetRange(1, end - 1);
            if (block.Count <= 1)
            {
                return;
            }

            var summary = await SummarizeAsync(block);
            if (summary is null)
            {
                return;
            }

            history.RemoveRange(1, end - 1);
            history.Insert(1, new Message.System($"[Summary of earlier conversation] {summary}"));
            InvalidateAnchor();
        }
    }

    private bool RemoveOldestBlock(List<Message> history, int seedLength)
    {
        var start = FirstRemovable(history, seedLength);
        if (start < 0)
        {
            return false;
        }

        var end = BlockEnd(history, start);
        history.RemoveRange(1, end - 1);
        InvalidateAnchor();
        return true;
    }

    private static int FirstRemovable(List<Message> history, int seedLength)
    {
        for (var i = Math.Max(1, seedLength - 1); i < history.Count; i++)
        {
            if (history[i] is Message.Assistant)
            {
                return i;
            }
        }

        return -1;
    }

    private static int BlockEnd(List<Message> history, int start)
    {
        var end = start + 1;
        while (end < history.Count && history[end] is Message.ToolResult)
        {
            end++;
        }

        return end;
    }

    private void InvalidateAnchor()
    {
        lock (_gate)
        {
            _anchorTokens = 0;
            _anchorLen = 0;
        }
    }

    private async Task<string?> SummarizeAsync(IReadOnlyList<Message> block)
    {
        var rendered = new StringBuilder();
        foreach (var message in block)
        {
            rendered.AppendLine(Render(message));
        }

        var request = new List<Message>
        {
            new Message.System("Summarize the conversation excerpt concisely; keep facts needed to continue."),
            new Message.User(rendered.ToString()),
        };

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TimeoutSecs));
            var completion = await _provider.CompleteAsync(request, [], new JsonObject(), cts.Token, _ => { });
            return completion.Response is Response.Text text ? text.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Render(Message message) => message switch
    {
        Message.System system => $"system: {system.Text}",
        Message.User user => $"user: {user.Text}",
        Message.Assistant assistant =>
            $"assistant: {assistant.Text ?? string.Empty}"
            + (assistant.ToolCalls.Count == 0 ? string.Empty : $" [tools: {string.Join(", ", assistant.ToolCalls.Select(c => c.Name))}]"),
        Message.ToolResult result => $"tool result: {result.Result}",
        _ => string.Empty,
    };

    private static int EstimateTokens(string text) => (text.Length + 3) / 4;

    private static int EstimateMessageTokens(Message message) => message switch
    {
        Message.System system => EstimateTokens(system.Text) + 4,
        Message.User user => EstimateTokens(user.Text) + 4,
        Message.Assistant assistant => EstimateTokens(assistant.Text ?? string.Empty)
            + assistant.ToolCalls.Sum(call => EstimateTokens(call.Args.ToJsonString()) + 8) + 4,
        Message.ToolResult result => EstimateTokens(result.Result) + 4,
        _ => 4,
    };

    private readonly record struct ToolRunResult(ToolCall Call, string Content, bool Cancelled, string? Error);
}
