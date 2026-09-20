namespace Icarus.Core.Tools;

/// <summary>
/// Recursive, hidden- and gitignore-aware file walk for the <c>read</c> tool's
/// directory/glob mode (ICARUS-104). Symlinked directories are not followed, so
/// the walk cannot loop.
/// </summary>
internal static class TreeWalk
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
    };

    /// <summary>
    /// Enumerate files under <paramref name="root"/> matching <paramref name="globs"/>.
    /// Glob entries with a leading <c>!</c> are exclusions; an empty glob list
    /// matches every non-ignored file. Returns absolute paths, sorted.
    /// </summary>
    public static IReadOnlyList<string> Files(string root, IReadOnlyList<string> globs)
    {
        var includes = globs.Where(g => !PathGlob.IsExclusion(g)).ToList();
        var excludes = globs.Where(PathGlob.IsExclusion).Select(g => g[1..]).ToList();

        var results = new List<string>();
        Walk(root, root, [], includes, excludes, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void Walk(
        string root,
        string directory,
        List<PathGlob.Rule> inheritedRules,
        IReadOnlyList<string> includes,
        IReadOnlyList<string> excludes,
        List<string> results)
    {
        var rules = new List<PathGlob.Rule>(inheritedRules);
        var gitignore = Path.Combine(directory, ".gitignore");
        if (File.Exists(gitignore))
        {
            rules.AddRange(PathGlob.ParseGitignore(File.ReadAllText(gitignore)));
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", Options))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.'))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');

            if (Directory.Exists(entry))
            {
                // Do not follow directory symlinks (cycle safety).
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (!PathGlob.IsIgnored(relative, rules))
                {
                    Walk(root, entry, rules, includes, excludes, results);
                }

                continue;
            }

            if (PathGlob.IsIgnored(relative, rules))
            {
                continue;
            }

            if (includes.Count > 0 && !includes.Any(g => PathGlob.IsMatch(g, relative)))
            {
                continue;
            }

            if (excludes.Any(g => PathGlob.IsMatch(g, relative)))
            {
                continue;
            }

            results.Add(entry);
        }
    }
}
