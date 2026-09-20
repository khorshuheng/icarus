using System.Text;
using Icarus.Core.Paths;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Skills;

/// <summary>Where a skill was discovered (workspace wins on a name clash).</summary>
public enum SkillLevel
{
    User,
    Workspace,
}

/// <summary>
/// A discovered skill (ICARUS-109): a markdown instruction file whose first
/// paragraph is the description and whose whole text is the body.
/// </summary>
public sealed record Skill(string Name, string Description, string Body, string Path, SkillLevel Level)
{
    /// <summary>The user message that loads this skill into the conversation.</summary>
    public string Prompt() => $"Follow these instructions:\n\n{Body}";
}

/// <summary>
/// Skill discovery and the system-prompt catalog (ICARUS-109). No frontmatter
/// parser; no new tool — the model loads a skill with <c>/skill</c> or by
/// reading the file.
/// </summary>
public static class SkillCatalog
{
    /// <summary>Longest description embedded in the catalog, in characters.</summary>
    public const int MaxDescription = 200;

    /// <summary>Discover user + workspace skills, workspace winning on a name clash.</summary>
    public static IReadOnlyList<Skill> Discover(Workspace workspace, string? userDirectory = null) =>
        DiscoverIn(userDirectory ?? IcarusPaths.SkillsDir, workspace);

    /// <summary>Discovery with an explicit user-level directory (testable).</summary>
    public static IReadOnlyList<Skill> DiscoverIn(string userDirectory, Workspace workspace)
    {
        var byName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        ReadInto(userDirectory, SkillLevel.User, byName);
        ReadInto(Path.Combine(workspace.Root, ".icarus", "skills"), SkillLevel.Workspace, byName);
        return byName.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The workspace-level skills directory for a workspace root.</summary>
    public static string WorkspaceDirectory(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".icarus", "skills");

    /// <summary>Append the catalog block to a base prompt (unchanged when there are no skills).</summary>
    public static string WithCatalog(string basePrompt, IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0)
        {
            return basePrompt;
        }

        var builder = new StringBuilder(basePrompt);
        builder.Append("\nAvailable skills: ");
        builder.Append(string.Join("; ", skills.Select(s =>
        {
            var readable = s.Level == SkillLevel.Workspace ? $" (also readable at {s.Path})" : string.Empty;
            return $"{s.Name} — {s.Description}{readable}";
        })));
        builder.Append(" (use /skill <name> to load one, or read the file when relevant)");
        return builder.ToString();
    }

    /// <summary>The first paragraph of a markdown file, whitespace-joined and capped.</summary>
    public static string Describe(string body)
    {
        var paragraph = new StringBuilder();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (paragraph.Length > 0)
                {
                    break;
                }

                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(line);
        }

        var text = paragraph.Length > 0 ? paragraph.ToString() : body.Trim().Split('\n')[0].Trim();
        return text.Length <= MaxDescription ? text : text[..MaxDescription];
    }

    private static void ReadInto(string directory, SkillLevel level, Dictionary<string, Skill> byName)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.md"))
        {
            string body;
            try
            {
                body = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 0)
            {
                continue;
            }

            byName[name] = new Skill(name, Describe(body), body, path, level);
        }
    }
}
