using Icarus.Core.Paths;

namespace Icarus.Core.Tests;

/// <summary>
/// Layering rules from ICARUS-100: the core is terminal-free, and platform
/// seams stay Linux-only.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] CoreReferences = typeof(IcarusPaths).Assembly
        .GetReferencedAssemblies()
        .Select(a => a.Name ?? string.Empty)
        .ToArray();

    [Fact]
    public void Core_does_not_reference_terminal_gui()
    {
        Assert.DoesNotContain("Terminal.Gui", CoreReferences);
    }

    [Fact]
    public void Core_references_no_other_tui_kit()
    {
        Assert.DoesNotContain("Spectre.Console", CoreReferences);
        Assert.DoesNotContain("Consolonia", CoreReferences);
    }
}
