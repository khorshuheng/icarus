using Icarus.Core.Theme;
using Terminal.Gui.Drawing;
using TgAttribute = Terminal.Gui.Drawing.Attribute;
using ThemeType = Icarus.Core.Theme.Theme;

namespace Icarus.Cli.Tui;

/// <summary>
/// Maps the data-only ICARUS <see cref="ThemeType"/> onto Terminal.Gui
/// attributes and schemes (ICARUS-108). Indexed 0–15 map to the matching ANSI
/// name; 16–255 fall back to gray (Terminal.Gui has no 256-colour index).
/// </summary>
public static class ThemeMap
{
    private static readonly ColorName16[] AnsiNames =
    [
        ColorName16.Black,
        ColorName16.Red,
        ColorName16.Green,
        ColorName16.Yellow,
        ColorName16.Blue,
        ColorName16.Magenta,
        ColorName16.Cyan,
        ColorName16.Gray,
        ColorName16.DarkGray,
        ColorName16.BrightRed,
        ColorName16.BrightGreen,
        ColorName16.BrightYellow,
        ColorName16.BrightBlue,
        ColorName16.BrightMagenta,
        ColorName16.BrightCyan,
        ColorName16.White,
    ];

    public static Color ToColor(ThemeColor color) => color.Kind switch
    {
        ThemeColorKind.Rgb => new Color(color.R, color.G, color.B, 255),
        ThemeColorKind.Indexed when color.Index is >= 0 and < 16 => new Color(AnsiNames[color.Index]),
        ThemeColorKind.Indexed => new Color(ColorName16.Gray),
        _ => Color.None,
    };

    public static TextStyle ToStyle(ThemeTextStyle style) =>
        (style.HasFlag(ThemeTextStyle.Bold) ? TextStyle.Bold : TextStyle.None)
        | (style.HasFlag(ThemeTextStyle.Dim) ? TextStyle.Faint : TextStyle.None)
        | (style.HasFlag(ThemeTextStyle.Italic) ? TextStyle.Italic : TextStyle.None)
        | (style.HasFlag(ThemeTextStyle.Underlined) ? TextStyle.Underline : TextStyle.None)
        | (style.HasFlag(ThemeTextStyle.Reversed) ? TextStyle.Reverse : TextStyle.None)
        | (style.HasFlag(ThemeTextStyle.CrossedOut) ? TextStyle.Strikethrough : TextStyle.None);

    public static TgAttribute ToAttribute(StyleSpec spec) =>
        new(ToColor(spec.Foreground), ToColor(spec.Background), ToStyle(spec.Style));

    /// <summary>
    /// Build a widget scheme from the theme. The transcript is a read-only
    /// <c>TextView</c>, so <c>ReadOnly</c> is mapped to the readable body color
    /// rather than Terminal.Gui's dim default.
    /// </summary>
    public static Scheme ToScheme(ThemeType theme)
    {
        var body = ToAttribute(theme[ThemeToken.Assistant]);
        var input = theme[ThemeToken.Input].Foreground.Kind == ThemeColorKind.Default
            ? body
            : ToAttribute(theme[ThemeToken.Input]);

        return new Scheme
        {
            Normal = body,
            HotNormal = body,
            Focus = input,
            HotFocus = input,
            Active = body,
            HotActive = body,
            Editable = input,
            ReadOnly = body,
            Disabled = body,
            Highlight = ToAttribute(theme[ThemeToken.Selection]),
        };
    }
}
