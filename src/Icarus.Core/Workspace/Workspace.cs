using Icarus.Core.Platform;

namespace Icarus.Core.Workspaces;

/// <summary>
/// The root that relative <c>read</c>/<c>write</c>/<c>edit</c> paths and
/// <c>bash</c> commands resolve against.
/// <para>
/// This is a <b>default location, not a security boundary</b> (ICARUS-105,
/// matching CRAB): absolute paths, <c>..</c>, and symlinks outside the root are
/// honored, because <c>bash</c> could always reach them. Path confusables that
/// models commonly emit are normalized before resolution.
/// </para>
/// </summary>
public sealed class Workspace
{
    private Workspace(string root) => Root = root;

    /// <summary>The canonical workspace root.</summary>
    public string Root { get; }

    /// <summary>Construct a workspace from <paramref name="root"/>, canonicalizing it.</summary>
    public static Workspace New(string root)
    {
        var canon = Posix.RealPath(root)
            ?? throw new ArgumentException($"workspace '{root}' cannot be resolved");
        if (!Directory.Exists(canon))
        {
            throw new ArgumentException($"workspace '{canon}' is not a directory");
        }

        return new Workspace(canon);
    }

    /// <summary>
    /// Resolve <paramref name="path"/>: normalize confusables, join relative
    /// paths to the root, and canonicalize the deepest existing ancestor
    /// (resolving symlinks) before re-appending any non-existent remainder.
    /// Absolute paths and <c>..</c> are honored.
    /// </summary>
    public string Resolve(string path)
    {
        var normalized = Normalize(path);

        var joined = Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(Root, normalized);

        // Find the deepest existing ancestor, canonicalize it, re-append the rest.
        var existing = joined;
        var suffix = new List<string>();
        while (!File.Exists(existing) && !Directory.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null || parent == existing)
            {
                break;
            }

            suffix.Add(Path.GetFileName(existing));
            existing = parent;
        }

        var canonical = Posix.RealPath(existing)
            ?? Path.GetFullPath(existing);
        suffix.Reverse();
        return suffix.Aggregate(canonical, Path.Combine);
    }

    /// <summary>
    /// Normalize path confusables: strip a leading <c>@</c>, map Unicode spaces
    /// to a regular space, and expand a leading <c>~</c>/<c>~/</c> against the
    /// home directory (<c>~user</c> and interior <c>~</c> are left untouched).
    /// </summary>
    public static string Normalize(string path) => ExpandTilde(CollapseSpaces(StripAt(path)));

    private static string StripAt(string path) =>
        path.StartsWith('@') ? path[1..] : path;

    private static string CollapseSpaces(string path)
    {
        var buffer = new char[path.Length];
        for (var i = 0; i < path.Length; i++)
        {
            buffer[i] = IsUnicodeSpace(path[i]) ? ' ' : path[i];
        }

        return new string(buffer);
    }

    private static bool IsUnicodeSpace(char c) => c switch
    {
        '\u00A0' or '\u202F' or '\u205F' or '\u3000' => true,
        >= '\u2000' and <= '\u200A' => true,
        _ => false,
    };

    private static string ExpandTilde(string path)
    {
        var home = Icarus.Core.Paths.IcarusPaths.Home;
        if (home is null)
        {
            return path;
        }

        if (path is "~" or "~/")
        {
            return home;
        }

        return path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(home, path[2..])
            : path;
    }
}
