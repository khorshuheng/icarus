using System.Text;
using System.Text.Json.Nodes;
using Icarus.Cli.Markdown;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;

namespace Icarus.Cli.Ui;

/// <summary>
/// The terminal-free UI model (ICARUS-107): folds runtime events into a
/// transcript and footer state. The Terminal.Gui views only render this.
/// </summary>
public sealed class UiModel(RuntimeState initial)
{
    private readonly StringBuilder _assistant = new();
    private readonly StringBuilder _thinking = new();

    public List<TranscriptLine> Transcript { get; } = [];

    public RuntimeState State { get; private set; } = initial;

    public int? PromptTokens { get; private set; }

    public int Turns { get; private set; }

    public int Queued { get; private set; }

    public IReadOnlyList<string> Models { get; private set; } = [];

    public TranscriptScroll Scroll { get; } = new();

    /// <summary>The in-progress assistant text (rendered live).</summary>
    public string LiveAssistant => _assistant.ToString();

    /// <summary>The in-progress reasoning text (rendered live).</summary>
    public string LiveThinking => _thinking.ToString();

    public bool Busy => State.Busy;

    /// <summary>Everything to render, including the live streaming buffers.</summary>
    public IReadOnlyList<TranscriptLine> Render(int width = 100)
    {
        var lines = new List<TranscriptLine>(Transcript.Count + 2);
        foreach (var line in Transcript)
        {
            lines.Add(line.Role switch
            {
                TranscriptRole.Assistant => line with { Text = MarkdownText.ToText(line.Text, width) },
                TranscriptRole.Thinking => line with { Text = "✻ " + line.Text },
                _ => line,
            });
        }

        if (_thinking.Length > 0)
        {
            lines.Add(new TranscriptLine(TranscriptRole.Thinking, "✻ " + _thinking.ToString()));
        }

        if (_assistant.Length > 0)
        {
            lines.Add(new TranscriptLine(TranscriptRole.Assistant, MarkdownText.ToText(_assistant.ToString(), width)));
        }

        return lines;
    }

    /// <summary>Fold one runtime event into the model.</summary>
    public void Apply(Event @event)
    {
        switch (@event)
        {
            case AgentStartEvent start:
                State = State with { Model = start.Model, Effort = start.Effort, Workspace = start.Workspace };
                break;
            case TurnStartEvent:
                Turns++;
                Flush();
                State = State with { Busy = true };
                Scroll.FollowTail();
                break;
            case ThinkingDeltaEvent thinking:
                if (_assistant.Length > 0)
                {
                    FlushAssistant();
                }

                _thinking.Append(thinking.Text);
                Scroll.FollowTail();
                break;
            case TextDeltaEvent text:
                if (_thinking.Length > 0)
                {
                    FlushThinking();
                }

                _assistant.Append(text.Text);
                Scroll.FollowTail();
                break;
            case ToolStartEvent tool:
                Flush();
                Transcript.Add(new TranscriptLine(TranscriptRole.Tool, ToolSummary(tool)));
                Scroll.FollowTail();
                break;
            case ToolEndEvent end when !end.Ok:
                Transcript.Add(new TranscriptLine(
                    TranscriptRole.Notice, $"✗ {end.Name}: {end.Error ?? "failed"}"));
                break;
            case TurnEndEvent:
                Flush();
                break;
            case UsageEvent usage:
                PromptTokens = usage.PromptTokens;
                break;
            case QueueUpdateEvent queue:
                Queued = queue.Queued;
                break;
            case StateChangedEvent changed:
                State = State with
                {
                    Model = changed.Model,
                    Provider = changed.Provider,
                    Effort = changed.Effort,
                    Workspace = changed.Workspace,
                };
                break;
            case ModelsListedEvent listed:
                Models = listed.Models;
                break;
            case AgentSettledEvent settled:
                Flush();
                Queued = 0;
                State = State with { Busy = false };
                if (settled.Interrupted)
                {
                    Transcript.Add(new TranscriptLine(TranscriptRole.Notice, "(interrupted)"));
                }

                Scroll.FollowTail();
                break;
            case ErrorEvent error:
                Flush();
                Transcript.Add(new TranscriptLine(TranscriptRole.Notice, error.Message));
                Scroll.FollowTail();
                break;
        }
    }

    public void PushUser(string text)
    {
        Flush();
        Transcript.Add(new TranscriptLine(TranscriptRole.User, text));
        Scroll.FollowTail();
    }

    public void PushNotice(string text)
    {
        Flush();
        Transcript.Add(new TranscriptLine(TranscriptRole.Notice, text));
        Scroll.FollowTail();
    }

    /// <summary>Repaint a loaded conversation (session resume).</summary>
    public void LoadHistory(IReadOnlyList<Message> history)
    {
        Transcript.Clear();
        _assistant.Clear();
        _thinking.Clear();
        foreach (var message in history)
        {
            switch (message)
            {
                case Message.User user:
                    Transcript.Add(new TranscriptLine(TranscriptRole.User, user.Text));
                    break;
                case Message.Assistant { Text.Length: > 0 } assistant:
                    Transcript.Add(new TranscriptLine(TranscriptRole.Assistant, assistant.Text!));
                    break;
                case Message.Assistant assistant:
                    foreach (var call in assistant.ToolCalls)
                    {
                        Transcript.Add(new TranscriptLine(TranscriptRole.Tool, ToolSummary(call.Name, call.Args)));
                    }

                    break;
            }
        }

        Scroll.FollowTail();
    }

    private void Flush()
    {
        FlushThinking();
        FlushAssistant();
    }

    private void FlushThinking()
    {
        if (_thinking.Length > 0)
        {
            Transcript.Add(new TranscriptLine(TranscriptRole.Thinking, _thinking.ToString()));
            _thinking.Clear();
        }
    }

    private void FlushAssistant()
    {
        if (_assistant.Length > 0)
        {
            Transcript.Add(new TranscriptLine(TranscriptRole.Assistant, _assistant.ToString()));
            _assistant.Clear();
        }
    }

    private static string ToolSummary(ToolStartEvent tool) => ToolSummary(tool.Name, tool.Args);

    private static string ToolSummary(string name, JsonNode? args)
    {
        if (name == "bash" && args?["command"]?.GetValue<string>() is { Length: > 0 } command)
        {
            return $"$ {command}";
        }

        if (args?["path"]?.GetValue<string>() is { Length: > 0 } path)
        {
            return $"{name} {path}";
        }

        return name;
    }
}
