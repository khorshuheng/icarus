namespace Icarus.Cli.Ui;

/// <summary>
/// A selection list for the slash-command pickers (ICARUS-108). Terminal-free
/// and cyclic, so the shell only has to render <see cref="Current"/>.
/// </summary>
public sealed class Picker
{
    public Picker(string title, IReadOnlyList<string> items, int selected = 0)
    {
        Title = title;
        Items = items;
        Selected = items.Count == 0 ? 0 : Math.Clamp(selected, 0, items.Count - 1);
    }

    public string Title { get; }

    public IReadOnlyList<string> Items { get; }

    public int Selected { get; private set; }

    public bool IsEmpty => Items.Count == 0;

    public string? Current => Items.Count == 0 ? null : Items[Selected];

    public void MoveUp()
    {
        if (Items.Count > 0)
        {
            Selected = (Selected - 1 + Items.Count) % Items.Count;
        }
    }

    public void MoveDown()
    {
        if (Items.Count > 0)
        {
            Selected = (Selected + 1) % Items.Count;
        }
    }

    public void Select(int index)
    {
        if (Items.Count > 0)
        {
            Selected = Math.Clamp(index, 0, Items.Count - 1);
        }
    }

    /// <summary>The accepted value (<c>""</c> when empty).</summary>
    public string Accept() => Current ?? string.Empty;

    /// <summary>The footer hint for the picker line.</summary>
    public string Hint() => IsEmpty
        ? $"{Title}: (none)"
        : $"{Title}: {Current}  ({Selected + 1}/{Items.Count}) · ↑/↓ · Enter apply · Esc cancel";
}
