namespace Icarus.Cli.Ui;

/// <summary>The role of a transcript line (ICARUS-107/139).</summary>
public enum TranscriptRole
{
    User,
    Assistant,
    Thinking,
    Tool,
    Notice,
}

/// <summary>One rendered transcript line.</summary>
public sealed record TranscriptLine(TranscriptRole Role, string Text);

/// <summary>
/// Transcript scrollback (ICARUS-107): <see cref="Top"/> is the first visible
/// row; following the tail is on until the user scrolls up.
/// </summary>
public sealed class TranscriptScroll
{
    public int Top { get; private set; }

    public bool Follow { get; private set; } = true;

    /// <summary>Scroll by <paramref name="lines"/> (negative = up); scrolling up stops following.</summary>
    public void ScrollBy(int lines)
    {
        Top = Math.Max(0, Top + lines);
        if (lines < 0)
        {
            Follow = false;
        }
    }

    public void PageUp(int page) => ScrollBy(-Math.Max(1, page));

    public void PageDown(int page) => ScrollBy(Math.Max(1, page));

    public void JumpToTop()
    {
        Top = 0;
        Follow = false;
    }

    public void FollowTail() => Follow = true;

    /// <summary>The effective first visible row for a transcript of <paramref name="total"/> lines.</summary>
    public int Resolve(int total, int viewport)
    {
        var max = Math.Max(0, total - viewport);
        if (Follow)
        {
            Top = max;
        }
        else
        {
            Top = Math.Clamp(Top, 0, max);
        }

        return Top;
    }
}
