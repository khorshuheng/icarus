using System.Text;
using System.Text.RegularExpressions;

namespace Icarus.Core.Tools;

/// <summary>
/// Glob and gitignore matching for the <c>read</c> tool's directory traversal
/// (ICARUS-104). Supports <c>*</c>, <c>**</c> and <c>?</c>; a pattern without a
/// slash matches the basename at any depth; a leading <c>!</c> negates.
/// </summary>
internal static class PathGlob
{
    private static readonly Dictionary<string, Regex> Cache = [];
    private static readonly Lock Gate = new();

    /// <summary>True when <paramref name="pattern"/> is an exclusion (leading <c>!</c>).</summary>
    public static bool IsExclusion(string pattern) => pattern.StartsWith('!');

    /// <summary>Match a glob <paramref name="pattern"/> against a '/'-separated relative path.</summary>
    public static bool IsMatch(string pattern, string path)
    {
        var normalized = pattern.Replace('\\', '/').TrimStart('/');
        var target = path.Replace('\\', '/');
        var basenameOnly = !normalized.Contains('/');
        return Compile(normalized, basenameOnly).IsMatch(target);
    }

    /// <summary>One parsed gitignore rule.</summary>
    public sealed record Rule(string Pattern, bool Negated, bool DirectoryOnly);

    /// <summary>Parse the non-comment, non-blank lines of a gitignore file.</summary>
    public static IEnumerable<Rule> ParseGitignore(string contents)
    {
        foreach (var raw in contents.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negated = line.StartsWith('!');
            if (negated)
            {
                line = line[1..];
            }

            var directoryOnly = line.EndsWith('/');
            line = line.Trim('/');
            if (line.Length > 0)
            {
                yield return new Rule(line, negated, directoryOnly);
            }
        }
    }

    /// <summary>
    /// Gitignore semantics: the last matching rule wins; directory-only rules
    /// match any ancestor directory of the path.
    /// </summary>
    public static bool IsIgnored(string relativePath, IReadOnlyList<Rule> rules)
    {
        bool? ignored = null;
        foreach (var rule in rules)
        {
            var matched = rule.DirectoryOnly
                ? Prefixes(relativePath).Any(prefix => IsMatch(rule.Pattern, prefix))
                : IsMatch(rule.Pattern, relativePath);
            if (matched)
            {
                ignored = !rule.Negated;
            }
        }

        return ignored ?? false;
    }

    private static IEnumerable<string> Prefixes(string relativePath)
    {
        var index = relativePath.IndexOf('/');
        while (index >= 0)
        {
            yield return relativePath[..index];
            index = relativePath.IndexOf('/', index + 1);
        }
    }

    private static Regex Compile(string pattern, bool basenameOnly)
    {
        var key = (basenameOnly ? "b:" : "r:") + pattern;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var regex = new Regex(Build(pattern, basenameOnly), RegexOptions.Compiled);
            Cache[key] = regex;
            return regex;
        }
    }

    private static string Build(string pattern, bool basenameOnly)
    {
        var builder = new StringBuilder(basenameOnly ? "(?:^|.*/)" : "^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        builder.Append('$');
        return builder.ToString();
    }
}
