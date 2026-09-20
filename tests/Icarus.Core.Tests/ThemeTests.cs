using Icarus.Core.Config;
using Icarus.Core.Theme;
using ThemeType = Icarus.Core.Theme.Theme;

namespace Icarus.Core.Tests;

public class ThemeTests
{
    [Fact]
    public void Parses_colors()
    {
        Assert.Equal(ThemeColor.Default, ThemeColor.Parse(""));
        Assert.Equal(ThemeColor.Default, ThemeColor.Parse("default"));
        Assert.Equal(ThemeColor.Indexed(6), ThemeColor.Parse("cyan"));
        Assert.Equal(ThemeColor.Indexed(14), ThemeColor.Parse("bright_cyan"));
        Assert.Equal(ThemeColor.Indexed(8), ThemeColor.Parse("dark_gray"));
        Assert.Equal(ThemeColor.Indexed(123), ThemeColor.Parse("123"));
        Assert.Equal(ThemeColor.Rgb(0x12, 0x34, 0x56), ThemeColor.Parse("#123456"));
        Assert.Throws<FormatException>(() => ThemeColor.Parse("not-a-color"));
        Assert.Throws<FormatException>(() => ThemeColor.Parse("#12"));
    }

    [Fact]
    public void Presets_have_the_role_tokens()
    {
        Assert.Equal(19, ThemeType.TokenNames.Count);
        Assert.Equal(ThemeColor.Indexed(6), ThemeType.Dark[ThemeToken.User].Foreground);
        Assert.Equal(ThemeTextStyle.Italic, ThemeType.Dark[ThemeToken.Thinking].Style);
        Assert.Equal(ThemeColor.Indexed(4), ThemeType.Light[ThemeToken.User].Foreground);
        Assert.Equal(ThemeColor.Indexed(0), ThemeType.Light[ThemeToken.Assistant].Foreground);
    }

    [Fact]
    public void Parses_a_token_style_with_vars()
    {
        var style = ThemeType.ParseStyle(
            "accent on 232 bold italic",
            new Dictionary<string, string> { ["accent"] = "#ff8800" });

        Assert.Equal(ThemeColor.Rgb(0xff, 0x88, 0x00), style.Foreground);
        Assert.Equal(ThemeColor.Indexed(232), style.Background);
        Assert.Equal(ThemeTextStyle.Bold | ThemeTextStyle.Italic, style.Style);
    }

    [Fact]
    public void Applies_overrides_on_top_of_a_preset()
    {
        var partial = new ThemePartial
        {
            Vars = new Dictionary<string, string> { ["accent"] = "#ff0000" },
            Tokens = new Dictionary<string, string> { ["user"] = "accent bold" },
        };

        var theme = ThemeType.Dark.WithOverrides(partial);

        Assert.Equal(ThemeColor.Rgb(0xff, 0, 0), theme[ThemeToken.User].Foreground);
        Assert.Equal(ThemeTextStyle.Bold, theme[ThemeToken.User].Style);
        Assert.Equal(ThemeType.Dark[ThemeToken.Assistant], theme[ThemeToken.Assistant]);
    }

    [Fact]
    public void Rejects_unknown_tokens()
    {
        var partial = new ThemePartial { Tokens = new Dictionary<string, string> { ["nope"] = "red" } };

        Assert.Throws<FormatException>(() => ThemeType.Dark.WithOverrides(partial));
    }

    [Fact]
    public void Unknown_preset_names_fail_at_config_load()
    {
        Assert.Null(ThemeType.Builtin("solarized"));
        Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws",
            new PartialConfig { Model = "m" },
            new PartialConfig { ThemeName = "solarized" },
            null));
    }

    [Fact]
    public void Config_resolves_theme_overrides_from_the_file()
    {
        using var file = TempFile.Write("""
            model = "m"

            [theme]
            name = "light"

            [theme.vars]
            accent = "#00ff00"

            [theme.tokens]
            notice = "accent bold"
            """);

        var config = ConfigLoader.Resolve("/tmp/ws", ConfigLoader.LoadFile(file.Path), new PartialConfig(), null);

        Assert.Equal("light", config.Theme.Name);
        Assert.Equal(ThemeColor.Rgb(0, 0xff, 0), config.Theme[ThemeToken.Notice].Foreground);
        Assert.Equal(ThemeTextStyle.Bold, config.Theme[ThemeToken.Notice].Style);
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Write(string contents)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "icarus-theme-" + Guid.NewGuid().ToString("N") + ".toml");
            File.WriteAllText(path, contents);
            return new TempFile(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
