using Icarus.Cli.Ui;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Icarus.Cli.Tui;

/// <summary>
/// A bordered overlay list used for the slash-command dropdown and the command
/// pickers (ICARUS-108). Terminal.Gui's built-in autocomplete did not appear,
/// so the shell draws and drives its own popup: a title bar, one row per item,
/// and a highlighted selection, with the theme's role attributes.
/// </summary>
public sealed class ListOverlay : View
{
    private Picker _picker = new(string.Empty, []);
    private int _offset;
    private TgAttribute _normal = new();
    private TgAttribute _selected = new();
    private TgAttribute _border = new();
    private TgAttribute _title = new();

    public ListOverlay()
    {
        CanFocus = false;
        Visible = false;
    }

    public bool IsOpen => Visible;

    public int Selected => _picker.Selected;

    public string? Current => _picker.Current;

    public void Configure(TgAttribute normal, TgAttribute selected, TgAttribute border, TgAttribute title)
    {
        _normal = normal;
        _selected = selected;
        _border = border;
        _title = title;
    }

    /// <summary>Open with <paramref name="items"/>; a modal list is centred, a dropdown sits above the input.</summary>
    public void Open(string title, IReadOnlyList<string> items, bool modal)
    {
        _picker = new Picker(title, items);
        _offset = 0;
        Visible = items.Count > 0;

        if (modal)
        {
            X = Pos.Percent(15);
            Width = Dim.Percent(70);
            Y = Pos.Percent(25);
        }
        else
        {
            X = 1;
            Width = Dim.Fill(2);
            Y = Pos.AnchorEnd(2 + Math.Min(items.Count + 2, 10));
        }

        Height = Math.Min(items.Count + 2, 10);
        SetNeedsDraw();
    }

    public void Close()
    {
        Visible = false;
        SetNeedsDraw();
    }

    public void MoveUp()
    {
        _picker.MoveUp();
        EnsureVisible();
        SetNeedsDraw();
    }

    public void MoveDown()
    {
        _picker.MoveDown();
        EnsureVisible();
        SetNeedsDraw();
    }

    private int VisibleRows => Math.Max(1, Viewport.Height - 2);

    private void EnsureVisible()
    {
        if (_picker.Selected < _offset)
        {
            _offset = _picker.Selected;
        }
        else if (_picker.Selected >= _offset + VisibleRows)
        {
            _offset = _picker.Selected - VisibleRows + 1;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!Visible || _picker.Items.Count == 0)
        {
            return false;
        }

        var width = Viewport.Width;
        var height = Viewport.Height;
        if (width < 4 || height < 3)
        {
            return false;
        }

        SetAttribute(_border);
        Move(0, 0);
        AddStr("┌" + new string('─', width - 2) + "┐");
        for (var row = 1; row < height - 1; row++)
        {
            Move(0, row);
            AddStr("│");
            Move(width - 1, row);
            AddStr("│");
        }

        Move(0, height - 1);
        AddStr("└" + new string('─', width - 2) + "┘");

        if (_picker.Title.Length > 0)
        {
            Move(2, 0);
            SetAttribute(_title);
            AddStr(" " + Clip(_picker.Title, width - 6) + " ");
        }

        var rows = height - 2;
        for (var i = 0; i < rows && _offset + i < _picker.Items.Count; i++)
        {
            var index = _offset + i;
            Move(1, i + 1);
            SetAttribute(index == _picker.Selected ? _selected : _normal);
            AddStr(Pad(Clip(_picker.Items[index], width - 2), width - 2));
        }

        return true;
    }

    private static string Clip(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        return text.Length <= width ? text : text[..width];
    }

    private static string Pad(string text, int width) =>
        text.Length >= width ? text : text.PadRight(width);
}
