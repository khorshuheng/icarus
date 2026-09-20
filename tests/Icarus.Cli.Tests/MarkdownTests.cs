using Icarus.Cli.Markdown;
using Icarus.Cli.Ui;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;

namespace Icarus.Cli.Tests;

public class MarkdownTextTests
{
    [Fact]
    public void Renders_headings_without_markers()
    {
        var text = MarkdownText.ToText("# Title\n\nbody", 40);

        Assert.Contains("Title", text);
        Assert.DoesNotContain("#", text);
    }

    [Fact]
    public void Strips_emphasis_and_code_markers()
    {
        var text = MarkdownText.ToText("**bold** and *italic* and `code`", 40);

        Assert.Contains("bold", text);
        Assert.Contains("italic", text);
        Assert.Contains("code", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("`", text);
    }

    [Fact]
    public void Renders_fenced_code_without_fences()
    {
        var text = MarkdownText.ToText("```csharp\nvar x = 1;\n```", 40);

        Assert.Contains("  var x = 1;", text);
        Assert.DoesNotContain("```", text);
    }

    [Fact]
    public void Renders_lists()
    {
        var text = MarkdownText.ToText("- one\n- two\n\n1. first\n2. second", 40);

        Assert.Contains("• one", text);
        Assert.Contains("• two", text);
        Assert.Contains("1. first", text);
        Assert.Contains("2. second", text);
    }

    [Fact]
    public void Renders_quotes_and_rules()
    {
        var text = MarkdownText.ToText("> quoted\n\n---", 10);

        Assert.Contains("│ quoted", text);
        Assert.Contains("──────────", text);
    }

    [Fact]
    public void Renders_links_as_label_and_url()
    {
        var text = MarkdownText.ToText("[docs](https://example.com)", 60);

        Assert.Contains("docs (https://example.com)", text);
    }
}

public class UiModelMarkdownTests
{
    [Fact]
    public void Renders_assistant_markdown()
    {
        var model = new UiModel(new RuntimeState("m", "fake", Effort.Medium, "/w", false));
        model.Apply(new TurnStartEvent());
        model.Apply(new TextDeltaEvent("**done** with `code`"));
        model.Apply(new AgentSettledEvent("x", false));

        var rendered = string.Join("\n", model.Render(60).Select(l => l.Text));

        Assert.Contains("done with code", rendered);
        Assert.DoesNotContain("**", rendered);
    }
}
