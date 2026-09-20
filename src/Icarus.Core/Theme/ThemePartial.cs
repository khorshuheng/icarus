using Tomlyn.Model;

namespace Icarus.Core.Theme;

/// <summary>
/// The raw <c>[theme]</c> table from <c>config.toml</c> (ICARUS-108): an
/// optional preset name, reusable <c>vars</c>, and per-token overrides.
/// </summary>
public sealed record ThemePartial
{
    /// <summary>The preset name (<c>dark</c>/<c>light</c>), if set.</summary>
    public string? Name { get; init; }

    /// <summary>Named colors referenced by token values.</summary>
    public IReadOnlyDictionary<string, string>? Vars { get; init; }

    /// <summary>Per-token styles, keyed by token name.</summary>
    public IReadOnlyDictionary<string, string>? Tokens { get; init; }

    /// <summary>Parse a <c>[theme]</c> TOML table.</summary>
    public static ThemePartial? FromToml(TomlTable? table)
    {
        if (table is null)
        {
            return null;
        }

        return new ThemePartial
        {
            Name = Str(table, "name"),
            Vars = Section(table, "vars"),
            Tokens = Section(table, "tokens"),
        };
    }

    private static string? Str(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string text ? text : null;

    private static IReadOnlyDictionary<string, string>? Section(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTable section)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, entry) in section)
        {
            if (entry is string text)
            {
                result[name] = text;
            }
        }

        return result;
    }
}
