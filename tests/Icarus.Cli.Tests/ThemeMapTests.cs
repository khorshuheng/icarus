using Icarus.Cli.Tui;
using Icarus.Core.Theme;
using Terminal.Gui.Drawing;
using ThemeType = Icarus.Core.Theme.Theme;

namespace Icarus.Cli.Tests;

public class ThemeMapTests
{
    [Fact]
    public void Maps_ansi_indices_to_named_colors()
    {
        Assert.Equal(new Color(ColorName16.Cyan), ThemeMap.ToColor(ThemeColor.Indexed(6)));
        Assert.Equal(new Color(ColorName16.Gray), ThemeMap.ToColor(ThemeColor.Indexed(7)));
        Assert.Equal(new Color(ColorName16.White), ThemeMap.ToColor(ThemeColor.Indexed(15)));
        Assert.Equal(new Color(ColorName16.DarkGray), ThemeMap.ToColor(ThemeColor.Indexed(8)));
    }

    [Fact]
    public void Maps_rgb_and_default_colors()
    {
        Assert.Equal(new Color(0x12, 0x34, 0x56, 255), ThemeMap.ToColor(ThemeColor.Rgb(0x12, 0x34, 0x56)));
        Assert.Equal(Color.None, ThemeMap.ToColor(ThemeColor.Default));
    }

    [Fact]
    public void Maps_style_modifiers()
    {
        var style = ThemeMap.ToStyle(ThemeTextStyle.Bold | ThemeTextStyle.Italic | ThemeTextStyle.Dim);

        Assert.True(style.HasFlag(TextStyle.Bold));
        Assert.True(style.HasFlag(TextStyle.Italic));
        Assert.True(style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void The_dark_scheme_uses_a_readable_body_color_for_read_only_text()
    {
        var scheme = ThemeMap.ToScheme(ThemeType.Dark);

        // The transcript is a read-only TextView: ReadOnly must not be the dim
        // Terminal.Gui default (the "gray throughout" bug).
        Assert.Equal(scheme.Normal, scheme.ReadOnly);
        Assert.Equal(new Color(ColorName16.Gray), scheme.Normal.Foreground);
    }

    [Fact]
    public void The_light_scheme_uses_black_text()
    {
        var scheme = ThemeMap.ToScheme(ThemeType.Light);

        Assert.Equal(new Color(ColorName16.Black), scheme.Normal.Foreground);
    }
}
