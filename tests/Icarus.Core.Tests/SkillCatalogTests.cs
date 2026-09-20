using Icarus.Core.Skills;

namespace Icarus.Core.Tests;

public class SkillCatalogTests : IDisposable
{
    private readonly string _userDir = Path.Combine(
        Path.GetTempPath(), "icarus-skills-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Discovers_user_and_workspace_skills_with_workspace_winning()
    {
        using var ws = new TempWorkspace();
        Directory.CreateDirectory(_userDir);
        File.WriteAllText(Path.Combine(_userDir, "shared.md"), "User version.\n\nUser body.");
        File.WriteAllText(Path.Combine(_userDir, "user-only.md"), "User only.\n\nBody.");
        var workspaceSkills = SkillCatalog.WorkspaceDirectory(ws.Dir);
        Directory.CreateDirectory(workspaceSkills);
        File.WriteAllText(Path.Combine(workspaceSkills, "shared.md"), "Workspace version.\n\nWorkspace body.");

        var skills = SkillCatalog.DiscoverIn(_userDir, ws.Workspace);

        Assert.Equal(2, skills.Count);
        var shared = skills.Single(s => s.Name == "shared");
        Assert.Equal(SkillLevel.Workspace, shared.Level);
        Assert.Equal("Workspace version.", shared.Description);
        Assert.Equal(SkillLevel.User, skills.Single(s => s.Name == "user-only").Level);
    }

    [Fact]
    public void Missing_directories_discover_nothing()
    {
        using var ws = new TempWorkspace();

        Assert.Empty(SkillCatalog.DiscoverIn(_userDir, ws.Workspace));
    }

    [Fact]
    public void Describes_the_first_paragraph_and_caps_it()
    {
        Assert.Equal("First line. Second line.", SkillCatalog.Describe("First line.\nSecond line.\n\nThird."));
        Assert.Equal("Only line", SkillCatalog.Describe("Only line"));

        var longText = new string('x', 300);
        Assert.Equal(SkillCatalog.MaxDescription, SkillCatalog.Describe(longText).Length);
    }

    [Fact]
    public void Catalog_block_is_appended_only_when_there_are_skills()
    {
        var basePrompt = "base";

        Assert.Equal("base", SkillCatalog.WithCatalog(basePrompt, []));

        var skill = new Skill("demo", "A demo.", "body", "/w/.icarus/skills/demo.md", SkillLevel.Workspace);
        var withCatalog = SkillCatalog.WithCatalog(basePrompt, [skill]);

        Assert.StartsWith("base", withCatalog);
        Assert.Contains("demo — A demo.", withCatalog);
        Assert.Contains("also readable at /w/.icarus/skills/demo.md", withCatalog);
    }

    [Fact]
    public void Skill_prompt_loads_the_body()
    {
        var skill = new Skill("demo", "d", "Do the thing.", "/p", SkillLevel.User);

        Assert.Equal("Follow these instructions:\n\nDo the thing.", skill.Prompt());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_userDir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
