using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Icarus.Cli.Markdown;

/// <summary>
/// Renders assistant markdown to styled transcript text (ICARUS-109) over a
/// plain CommonMark subset: headings without <c>#</c>, emphasis markers
/// stripped, fenced code indented, nested bullet/ordered lists, blockquotes,
/// horizontal rules, and links as <c>label (url)</c>. GFM is off; the styling
/// tokens are applied by the caller.
/// </summary>
public static class MarkdownText
{
    public static string ToText(string markdown, int width)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var document = Markdig.Markdown.Parse(markdown, new MarkdownPipelineBuilder().Build());
        var builder = new StringBuilder();
        foreach (var block in document)
        {
            RenderBlock(block, builder, width, indent: 0);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void RenderBlock(Block block, StringBuilder builder, int width, int indent)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AppendWrapped(builder, RenderInlines(heading.Inline), indent);
                break;
            case ParagraphBlock paragraph:
                AppendWrapped(builder, RenderInlines(paragraph.Inline), indent);
                break;
            case ListBlock list:
                RenderList(list, builder, width, indent);
                return;
            case CodeBlock code when code is not FencedCodeBlock:
            case FencedCodeBlock:
                RenderCode((LeafBlock)block, builder, indent);
                break;
            case QuoteBlock quote:
                RenderQuote(quote, builder, width, indent);
                break;
            case ThematicBreakBlock:
                builder.Append(new string('─', Math.Max(1, width))).Append('\n');
                break;
        }

        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.Append('\n');
        }
    }

    private static void RenderList(ListBlock list, StringBuilder builder, int width, int indent)
    {
        var index = 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{index}. " : "• ";
            index++;
            var first = true;
            foreach (var child in item)
            {
                if (first && child is ParagraphBlock paragraph)
                {
                    builder.Append(new string(' ', indent)).Append(marker)
                        .Append(RenderInlines(paragraph.Inline)).Append('\n');
                    first = false;
                }
                else
                {
                    RenderBlock(child, builder, width, indent + 2);
                }
            }
        }
    }

    private static void RenderCode(LeafBlock code, StringBuilder builder, int indent)
    {
        var lines = code.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            builder.Append(new string(' ', indent + 2)).Append(lines.Lines[i].Slice.ToString()).Append('\n');
        }
    }

    private static void RenderQuote(QuoteBlock quote, StringBuilder builder, int width, int indent)
    {
        var inner = new StringBuilder();
        foreach (var child in quote)
        {
            RenderBlock(child, inner, width, 0);
        }

        foreach (var line in inner.ToString().TrimEnd('\n').Split('\n'))
        {
            builder.Append(new string(' ', indent)).Append("│ ").Append(line).Append('\n');
        }
    }

    private static void AppendWrapped(StringBuilder builder, string text, int indent)
    {
        if (text.Length == 0)
        {
            return;
        }

        builder.Append(new string(' ', indent)).Append(text).Append('\n');
    }

    private static string RenderInlines(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in container)
        {
            RenderInline(inline, builder);
        }

        return builder.ToString();
    }

    private static void RenderInline(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case EmphasisInline emphasis:
                foreach (var child in emphasis)
                {
                    RenderInline(child, builder);
                }

                break;
            case LinkInline link:
                var label = RenderInlines(link);
                var url = link.Url ?? string.Empty;
                builder.Append(label);
                if (url.Length > 0 && url != label)
                {
                    builder.Append(" (").Append(url).Append(')');
                }

                break;
            case LineBreakInline:
                builder.Append('\n');
                break;
            case ContainerInline nested:
                foreach (var child in nested)
                {
                    RenderInline(child, builder);
                }

                break;
        }
    }
}
