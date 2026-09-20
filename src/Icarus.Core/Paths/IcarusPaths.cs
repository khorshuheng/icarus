namespace Icarus.Core.Paths;

/// <summary>
/// XDG base directories for ICARUS (Linux only, ICARUS-105). Honours
/// <c>XDG_CONFIG_HOME</c>/<c>XDG_DATA_HOME</c> and falls back to
/// <c>~/.config</c>/<c>~/.local/share</c>. No platform fallbacks.
/// </summary>
public static class IcarusPaths
{
    private const string App = "icarus";

    /// <summary>The user's home directory, or <c>null</c> when unknown.</summary>
    public static string? Home { get; } = Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
            ? profile
            : null;

    /// <summary>The configuration directory (<c>$XDG_CONFIG_HOME/icarus</c>).</summary>
    public static string ConfigDir => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), App);

    /// <summary>The data directory (<c>$XDG_DATA_HOME/icarus</c>).</summary>
    public static string DataDir => Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), App);

    /// <summary>The default config file path.</summary>
    public static string ConfigFile => Path.Combine(ConfigDir, "config.toml");

    /// <summary>The default session root.</summary>
    public static string SessionRoot => Path.Combine(DataDir, "sessions");

    /// <summary>The user-level skills directory.</summary>
    public static string SkillsDir => Path.Combine(ConfigDir, "skills");

    private static string Xdg(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value) && value.StartsWith('/'))
        {
            return value;
        }

        return Home is null
            ? Path.Combine("/tmp", App)
            : Path.Combine(Home, fallback);
    }
}
