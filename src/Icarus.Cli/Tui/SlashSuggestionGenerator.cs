using Icarus.Cli.Ui;
using Terminal.Gui.Views;

namespace Icarus.Cli.Tui;

/// <summary>
/// Drives Terminal.Gui's popup autocomplete for slash commands (ICARUS-108):
/// typing a leading <c>/token</c> shows the matching commands, and accepting a
/// suggestion inserts <c>/name </c> ready for an argument.
/// </summary>
public sealed class SlashSuggestionGenerator : ISuggestionGenerator
{
    public IEnumerable<Suggestion> GenerateSuggestions(AutocompleteContext context)
    {
        var line = string.Concat(context.CurrentLine.Take(context.CursorPosition).Select(c => c.Grapheme));
        var start = line.LastIndexOfAny([' ', '\t']) + 1;
        var word = line[start..];
        if (!word.StartsWith('/'))
        {
            yield break;
        }

        foreach (var spec in SlashCommands.Complete(word))
        {
            yield return new Suggestion(
                word.Length,
                "/" + spec.Name + " ",
                spec.ArgumentHint is null ? spec.Name : $"{spec.Name} {spec.ArgumentHint}");
        }
    }

    public bool IsWordChar(string text) => text.Length > 0 && !char.IsWhiteSpace(text[0]);
}
