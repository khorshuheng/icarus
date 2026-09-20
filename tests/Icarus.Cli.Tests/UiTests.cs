using System.Text.Json.Nodes;
using Icarus.Cli.Ui;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;

namespace Icarus.Cli.Tests;

public class SlashCommandsTests
{
    [Fact]
    public void Parses_a_plain_message()
    {
        var action = SlashCommands.Parse("  hello world  ");

        Assert.Equal("hello world", Assert.IsType<LineAction.Message>(action).Text);
    }

    [Fact]
    public void Parses_a_command_and_its_argument()
    {
        var action = Assert.IsType<LineAction.Command>(SlashCommands.Parse("/model claude-sonnet-4"));

        Assert.Equal(SlashCommandKind.Model, action.Slash.Kind);
        Assert.Equal("claude-sonnet-4", action.Slash.Argument);
    }

    [Theory]
    [InlineData("/quit", SlashCommandKind.Exit)]
    [InlineData("/q", SlashCommandKind.Exit)]
    [InlineData("/EXIT", SlashCommandKind.Exit)]
    public void Resolves_aliases_case_insensitively(string input, SlashCommandKind expected)
    {
        var action = Assert.IsType<LineAction.Command>(SlashCommands.Parse(input));

        Assert.Equal(expected, action.Slash.Kind);
    }

    [Fact]
    public void An_unknown_command_is_reported()
    {
        var action = Assert.IsType<LineAction.Command>(SlashCommands.Parse("/nope"));

        Assert.Equal(SlashCommandKind.Unknown, action.Slash.Kind);
        Assert.Equal("nope", action.Slash.Argument);
    }

    [Fact]
    public void Completes_command_prefixes()
    {
        var matches = SlashCommands.Complete("/sk");

        Assert.Equal(["skill", "skills"], matches.Select(m => m.Name).OrderBy(n => n).ToArray());
        Assert.Empty(SlashCommands.Complete("/skill foo"));
        Assert.Empty(SlashCommands.Complete("hello"));
    }
}

public class InputEditorTests
{
    [Fact]
    public void Edits_at_the_cursor()
    {
        var editor = new InputEditor();
        editor.Insert("hello");
        editor.Left();
        editor.Left();
        editor.InsertChar('X');

        Assert.Equal("helXlo", editor.Text);
        Assert.Equal(4, editor.Cursor);

        editor.Backspace();
        Assert.Equal("hello", editor.Text);

        editor.Delete();
        Assert.Equal("helo", editor.Text);
    }

    [Fact]
    public void Supports_kill_bindings()
    {
        var editor = new InputEditor();
        editor.SetText("foo bar");
        editor.KillPrevWord();
        Assert.Equal("foo ", editor.Text);

        editor.SetText("hello");
        editor.Home();
        editor.Right();
        editor.Right();
        editor.KillToStart();
        Assert.Equal("llo", editor.Text);

        editor.SetText("hello");
        editor.Home();
        editor.Insert("he");
        editor.KillToEnd();
        Assert.Equal("he", editor.Text);
    }

    [Fact]
    public void Flattens_pasted_newlines()
    {
        var editor = new InputEditor();
        editor.Insert("a\r\nb");

        Assert.Equal("a  b", editor.Text);
    }

    [Fact]
    public void Windows_on_the_caret_and_stays_width_aware()
    {
        var editor = new InputEditor();
        editor.SetText("abcdef");
        editor.Home();

        var (visible, caret) = editor.Window(5);
        Assert.Equal("abcde", visible);
        Assert.Equal(0, caret);

        editor.End();
        (visible, caret) = editor.Window(5);
        Assert.Equal("cdef", visible);
        Assert.Equal(4, caret);
    }

    [Fact]
    public void Wide_characters_count_as_two_cells()
    {
        var editor = new InputEditor();
        editor.SetText("宽宽宽");

        editor.Home();
        var (visible, caret) = editor.Window(4);
        Assert.Equal("宽宽", visible);
        Assert.Equal(0, caret);

        editor.End();
        (visible, caret) = editor.Window(4);
        Assert.Equal("宽", visible);
        Assert.Equal(2, caret);
    }
}

public class TranscriptScrollTests
{
    [Fact]
    public void Follows_the_tail_by_default()
    {
        var scroll = new TranscriptScroll();

        Assert.Equal(10, scroll.Resolve(total: 20, viewport: 10));
    }

    [Fact]
    public void Scrolling_up_stops_following()
    {
        var scroll = new TranscriptScroll();
        scroll.Resolve(20, 10);

        scroll.ScrollBy(-3);
        Assert.False(scroll.Follow);
        Assert.Equal(7, scroll.Resolve(20, 10));

        scroll.FollowTail();
        Assert.Equal(10, scroll.Resolve(20, 10));
    }

    [Fact]
    public void Page_and_jump_are_clamped()
    {
        var scroll = new TranscriptScroll();
        scroll.PageUp(5);
        Assert.Equal(0, scroll.Resolve(20, 10));

        scroll.PageDown(100);
        Assert.Equal(10, scroll.Resolve(20, 10));

        scroll.JumpToTop();
        Assert.Equal(0, scroll.Resolve(20, 10));
    }
}

public class PickerTests
{
    [Fact]
    public void Cycles_through_items()
    {
        var picker = new Picker("effort", ["off", "low", "high"]);

        Assert.Equal("off", picker.Current);
        picker.MoveUp();
        Assert.Equal("high", picker.Current);
        picker.MoveDown();
        Assert.Equal("off", picker.Current);
        picker.Select(1);
        Assert.Equal("low", picker.Accept());
    }

    [Fact]
    public void Clamps_selection_and_handles_empty()
    {
        var picker = new Picker("model", ["a", "b"], selected: 5);
        Assert.Equal("b", picker.Current);

        var empty = new Picker("provider", []);
        Assert.True(empty.IsEmpty);
        Assert.Null(empty.Current);
        Assert.Equal(string.Empty, empty.Accept());
        empty.MoveDown();
        Assert.True(empty.IsEmpty);
    }
}

public class UiModelTests
{
    private static UiModel NewModel() =>
        new(new RuntimeState("m", "fake", Effort.Medium, "/work", false));

    [Fact]
    public void Folds_a_text_turn()
    {
        var model = NewModel();

        model.Apply(new TurnStartEvent());
        Assert.True(model.Busy);

        model.Apply(new ThinkingDeltaEvent("hmm"));
        Assert.Equal("hmm", model.LiveThinking);

        model.Apply(new TextDeltaEvent("hello"));
        Assert.Equal("hello", model.LiveAssistant);

        model.Apply(new UsageEvent(42));
        model.Apply(new TurnEndEvent());
        model.Apply(new AgentSettledEvent("hello", false));

        Assert.False(model.Busy);
        Assert.Equal(42, model.PromptTokens);
        Assert.Equal(1, model.Turns);
        var roles = model.Transcript.Select(l => l.Role).ToArray();
        Assert.Contains(TranscriptRole.Thinking, roles);
        Assert.Contains(TranscriptRole.Assistant, roles);
        Assert.Empty(model.LiveAssistant);
    }

    [Fact]
    public void Renders_a_bash_command_on_the_tool_line()
    {
        var model = NewModel();

        model.Apply(new ToolStartEvent("bash", "c1", new JsonObject { ["command"] = "ls -la" }));
        model.Apply(new ToolEndEvent("bash", true, null));

        Assert.Contains(model.Transcript, l => l is { Role: TranscriptRole.Tool, Text: "$ ls -la" });
    }

    [Fact]
    public void Records_a_failed_tool_and_interrupts()
    {
        var model = NewModel();

        model.Apply(new ToolEndEvent("read", false, "not found"));
        model.Apply(new AgentSettledEvent("", true));

        Assert.Contains(model.Transcript, l => l.Role == TranscriptRole.Notice && l.Text.Contains("not found"));
        Assert.Contains(model.Transcript, l => l.Text == "(interrupted)");
        Assert.False(model.Busy);
    }

    [Fact]
    public void Tracks_state_changes_and_the_queue()
    {
        var model = NewModel();

        model.Apply(new StateChangedEvent("m2", "anthropic", Effort.High, "/other"));
        model.Apply(new QueueUpdateEvent(2, "steer"));

        Assert.Equal("anthropic", model.State.Provider);
        Assert.Equal(Effort.High, model.State.Effort);
        Assert.Equal("/other", model.State.Workspace);
        Assert.Equal(2, model.Queued);
    }

    [Fact]
    public void Repaints_a_loaded_conversation()
    {
        var model = NewModel();

        model.LoadHistory(
        [
            new Message.System("seed"),
            new Message.User("hi"),
            Message.Assistant.OfToolCalls([new ToolCall("c1", "read", new JsonObject { ["path"] = "a.txt" })]),
            new Message.ToolResult("c1", "contents"),
            Message.Assistant.OfText("done"),
        ]);

        Assert.Contains(model.Transcript, l => l is { Role: TranscriptRole.User, Text: "hi" });
        Assert.Contains(model.Transcript, l => l.Text == "read a.txt");
        Assert.Contains(model.Transcript, l => l is { Role: TranscriptRole.Assistant, Text: "done" });
    }
}
