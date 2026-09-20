using System.Globalization;

namespace Icarus.Core.Theme;

/// <summary>The kind of a <see cref="ThemeColor"/>.</summary>
public enum ThemeColorKind
{
    /// <summary>The terminal's default foreground/background.</summary>
    Default,

    /// <summary>An ANSI index (0–255).</summary>
    Indexed,

    /// <summary>A 24-bit RGB colour.</summary>
    Rgb,
}

/// <summary>A terminal color, independent of any rendering crate (ICARUS-108).</summary>
public readonly record struct ThemeColor(ThemeColorKind Kind, int Index, byte R, byte G, byte B)
{
    public static readonly ThemeColor Default = new(ThemeColorKind.Default, 0, 0, 0, 0);

    public static ThemeColor Indexed(int index) => new(ThemeColorKind.Indexed, index, 0, 0, 0);

    public static ThemeColor Rgb(byte r, byte g, byte b) => new(ThemeColorKind.Rgb, 0, r, g, b);

    /// <summary>Parse <c>""</c>, an ANSI name/number, or <c>#rrggbb</c>.</summary>
    public static ThemeColor Parse(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        if (text.StartsWith('#'))
        {
            if (text.Length == 7
                && byte.TryParse(text[1..3], NumberStyles.HexNumber, null, out var r)
                && byte.TryParse(text[3..5], NumberStyles.HexNumber, null, out var g)
                && byte.TryParse(text[5..7], NumberStyles.HexNumber, null, out var b))
            {
                return Rgb(r, g, b);
            }

            throw new FormatException($"invalid color '{value}'");
        }

        if (int.TryParse(text, out var number) && number is >= 0 and <= 255)
        {
            return Indexed(number);
        }

        if (AnsiNames.TryGetValue(text.ToLowerInvariant(), out var index))
        {
            return Indexed(index);
        }

        throw new FormatException($"unknown color '{value}'");
    }

    private static readonly Dictionary<string, int> AnsiNames = new(StringComparer.Ordinal)
    {
        ["black"] = 0,
        ["red"] = 1,
        ["green"] = 2,
        ["yellow"] = 3,
        ["blue"] = 4,
        ["magenta"] = 5,
        ["cyan"] = 6,
        ["white"] = 7,
        ["gray"] = 7,
        ["grey"] = 7,
        ["bright_black"] = 8,
        ["dark_gray"] = 8,
        ["dark_grey"] = 8,
        ["bright_red"] = 9,
        ["bright_green"] = 10,
        ["bright_yellow"] = 11,
        ["bright_blue"] = 12,
        ["bright_magenta"] = 13,
        ["bright_cyan"] = 14,
        ["bright_white"] = 15,
    };

    public override string ToString() => Kind switch
    {
        ThemeColorKind.Rgb => $"#{R:x2}{G:x2}{B:x2}",
        ThemeColorKind.Indexed => Index.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}

/// <summary>Style modifiers for a token.</summary>
[Flags]
public enum ThemeTextStyle
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underlined = 8,
    Reversed = 16,
    CrossedOut = 32,
}

/// <summary>The foreground, background and modifiers of one role token.</summary>
public sealed record StyleSpec(ThemeColor Foreground, ThemeColor Background, ThemeTextStyle Style)
{
    public static readonly StyleSpec Empty = new(ThemeColor.Default, ThemeColor.Default, ThemeTextStyle.None);
}

/// <summary>The role tokens the TUI renders (ICARUS-108).</summary>
public enum ThemeToken
{
    Text,
    User,
    Assistant,
    Thinking,
    Tool,
    ToolOk,
    ToolErr,
    Notice,
    Border,
    Title,
    Spinner,
    Input,
    Selection,
    MdHeading,
    MdCode,
    MdCodeBlock,
    MdLink,
    MdQuote,
    MdBullet,
}

/// <summary>A resolved, data-only theme (ICARUS-108).</summary>
public sealed class Theme
{
    /// <summary>Every token name, in a stable order.</summary>
    public static readonly IReadOnlyList<(string Name, ThemeToken Token)> TokenNames =
    [
        ("text", ThemeToken.Text),
        ("user", ThemeToken.User),
        ("assistant", ThemeToken.Assistant),
        ("thinking", ThemeToken.Thinking),
        ("tool", ThemeToken.Tool),
        ("tool_ok", ThemeToken.ToolOk),
        ("tool_err", ThemeToken.ToolErr),
        ("notice", ThemeToken.Notice),
        ("border", ThemeToken.Border),
        ("title", ThemeToken.Title),
        ("spinner", ThemeToken.Spinner),
        ("input", ThemeToken.Input),
        ("selection", ThemeToken.Selection),
        ("md_heading", ThemeToken.MdHeading),
        ("md_code", ThemeToken.MdCode),
        ("md_code_block", ThemeToken.MdCodeBlock),
        ("md_link", ThemeToken.MdLink),
        ("md_quote", ThemeToken.MdQuote),
        ("md_bullet", ThemeToken.MdBullet),
    ];

    private readonly Dictionary<ThemeToken, StyleSpec> _tokens;

    private Theme(string name, Dictionary<ThemeToken, StyleSpec> tokens)
    {
        Name = name;
        _tokens = tokens;
    }

    /// <summary>The preset name (<c>dark</c>/<c>light</c>).</summary>
    public string Name { get; private set; }

    public StyleSpec this[ThemeToken token] => _tokens[token];

    /// <summary>The dark preset (reproduces CRAB's default look).</summary>
    public static Theme Dark { get; } = Build("dark", light: false);

    /// <summary>The light preset.</summary>
    public static Theme Light { get; } = Build("light", light: true);

    /// <summary>Look up a built-in preset by name, or <c>null</c>.</summary>
    public static Theme? Builtin(string name) => name.Trim().ToLowerInvariant() switch
    {
        "dark" => Dark,
        "light" => Light,
        _ => null,
    };

    /// <summary>
    /// Detect dark/light from <c>COLORFGBG</c> (best effort): a background index
    /// of 7 or higher is treated as a light terminal.
    /// </summary>
    public static Theme Detect()
    {
        var value = Environment.GetEnvironmentVariable("COLORFGBG");
        var parts = value?.Split(';');
        if (parts is { Length: >= 2 } && int.TryParse(parts[^1], out var background) && background >= 7)
        {
            return Light;
        }

        return Dark;
    }

    /// <summary>Return a copy with the partial's vars and token overrides applied.</summary>
    public Theme WithOverrides(ThemePartial? partial)
    {
        if (partial is null)
        {
            return this;
        }

        var tokens = new Dictionary<ThemeToken, StyleSpec>(_tokens);
        var vars = partial.Vars ?? new Dictionary<string, string>();

        foreach (var (name, value) in partial.Tokens ?? new Dictionary<string, string>())
        {
            var match = TokenNames.FirstOrDefault(t => t.Name == name);
            if (match.Name is null)
            {
                throw new FormatException($"unknown theme token '{name}'");
            }

            tokens[match.Token] = ParseStyle(value, vars);
        }

        return new Theme(Name, tokens) { Name = partial.Name ?? Name };
    }

    /// <summary>Parse a token style: <c>"&lt;fg&gt; [on &lt;bg&gt;] [mods…]"</c>.</summary>
    public static StyleSpec ParseStyle(string value, IReadOnlyDictionary<string, string> vars)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var foreground = ThemeColor.Default;
        var background = ThemeColor.Default;
        var style = ThemeTextStyle.None;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == "on" && i + 1 < parts.Length)
            {
                background = Resolve(parts[++i], vars);
                continue;
            }

            switch (part)
            {
                case "bold":
                    style |= ThemeTextStyle.Bold;
                    break;
                case "dim":
                    style |= ThemeTextStyle.Dim;
                    break;
                case "italic":
                    style |= ThemeTextStyle.Italic;
                    break;
                case "underlined" or "underline":
                    style |= ThemeTextStyle.Underlined;
                    break;
                case "reversed":
                    style |= ThemeTextStyle.Reversed;
                    break;
                case "crossed_out" or "crossed-out":
                    style |= ThemeTextStyle.CrossedOut;
                    break;
                default:
                    foreground = Resolve(part, vars);
                    break;
            }
        }

        return new StyleSpec(foreground, background, style);
    }

    private static ThemeColor Resolve(string value, IReadOnlyDictionary<string, string> vars) =>
        ThemeColor.Parse(vars.TryGetValue(value, out var mapped) ? mapped : value);

    private static Theme Build(string name, bool light)
    {
        var tokens = TokenNames.ToDictionary(t => t.Token, _ => StyleSpec.Empty);
        void Set(ThemeToken token, ThemeColor fg, ThemeTextStyle style = ThemeTextStyle.None) =>
            tokens[token] = new StyleSpec(fg, ThemeColor.Default, style);

        Set(ThemeToken.User, ThemeColor.Indexed(light ? 4 : 6));
        Set(ThemeToken.Assistant, ThemeColor.Indexed(light ? 0 : 7));
        Set(ThemeToken.Thinking, ThemeColor.Indexed(8), ThemeTextStyle.Italic);
        Set(ThemeToken.Tool, ThemeColor.Indexed(8));
        Set(ThemeToken.ToolOk, ThemeColor.Indexed(8));
        Set(ThemeToken.ToolErr, ThemeColor.Indexed(8));
        Set(ThemeToken.Notice, ThemeColor.Indexed(light ? 5 : 3));
        Set(ThemeToken.Selection, ThemeColor.Default, ThemeTextStyle.Bold);
        Set(ThemeToken.MdHeading, ThemeColor.Default, ThemeTextStyle.Bold);
        Set(ThemeToken.MdCode, ThemeColor.Indexed(light ? 5 : 3));
        Set(ThemeToken.MdCodeBlock, ThemeColor.Indexed(light ? 5 : 3));
        Set(ThemeToken.MdLink, ThemeColor.Indexed(4), ThemeTextStyle.Underlined);
        Set(ThemeToken.MdQuote, ThemeColor.Indexed(8), light ? ThemeTextStyle.None : ThemeTextStyle.Italic);
        Set(ThemeToken.MdBullet, ThemeColor.Indexed(8));

        return new Theme(name, tokens);
    }
}
