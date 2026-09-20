namespace Icarus.Cli.Ui;

/// <summary>
/// A single-line input editor (ICARUS-107/126): caret tracking, readline-style
/// kill bindings, and a width-aware window so the caret stays visible.
/// </summary>
public sealed class InputEditor
{
    private string _text = string.Empty;
    private int _cursor;

    public string Text => _text;

    public int Cursor => _cursor;

    public void SetText(string text)
    {
        _text = text;
        _cursor = text.Length;
    }

    /// <summary>Take the text and reset the editor.</summary>
    public string Take()
    {
        var text = _text;
        _text = string.Empty;
        _cursor = 0;
        return text;
    }

    public void Insert(string text)
    {
        // The editor is single-line: flatten pasted control characters.
        var flattened = text.Replace('\r', ' ').Replace('\n', ' ');
        _text = _text[.._cursor] + flattened + _text[_cursor..];
        _cursor += flattened.Length;
    }

    public void InsertChar(char value) => Insert(value.ToString());

    public void Backspace()
    {
        if (_cursor > 0)
        {
            _text = _text.Remove(_cursor - 1, 1);
            _cursor--;
        }
    }

    public void Delete()
    {
        if (_cursor < _text.Length)
        {
            _text = _text.Remove(_cursor, 1);
        }
    }

    public void Left() => _cursor = Math.Max(0, _cursor - 1);

    public void Right() => _cursor = Math.Min(_text.Length, _cursor + 1);

    public void Home() => _cursor = 0;

    public void End() => _cursor = _text.Length;

    /// <summary>Delete the previous word, readline-style (trailing spaces, then the word).</summary>
    public void KillPrevWord()
    {
        var end = _cursor;
        var start = end;
        while (start > 0 && char.IsWhiteSpace(_text[start - 1]))
        {
            start--;
        }

        while (start > 0 && !char.IsWhiteSpace(_text[start - 1]))
        {
            start--;
        }

        _text = _text.Remove(start, end - start);
        _cursor = start;
    }

    public void KillToStart()
    {
        _text = _text[_cursor..];
        _cursor = 0;
    }

    public void KillToEnd()
    {
        _text = _text[.._cursor];
    }

    /// <summary>The visible slice for a viewport of <paramref name="width"/> cells and the caret column.</summary>
    public (string Visible, int CursorColumn) Window(int width)
    {
        if (width <= 0)
        {
            return (string.Empty, 0);
        }

        var start = 0;
        var consumed = 0;
        for (var i = _cursor - 1; i >= 0; i--)
        {
            consumed += DisplayWidth.Of(_text[i]);
            if (consumed > width - 1)
            {
                start = i + 1;
                break;
            }
        }

        var builder = new System.Text.StringBuilder();
        var used = 0;
        for (var i = start; i < _text.Length; i++)
        {
            var cell = DisplayWidth.Of(_text[i]);
            if (used + cell > width)
            {
                break;
            }

            builder.Append(_text[i]);
            used += cell;
        }

        var caret = 0;
        for (var i = start; i < _cursor && i < _text.Length; i++)
        {
            caret += DisplayWidth.Of(_text[i]);
        }

        return (builder.ToString(), caret);
    }
}
