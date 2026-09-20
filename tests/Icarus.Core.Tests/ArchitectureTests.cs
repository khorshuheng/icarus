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

    [Fact]
    public void Core_uses_only_official_provider_clients()
    {
        // The official AWS SDK, Anthropic SDK and OpenAI SDK; no community wrappers.
        Assert.Contains("AWSSDK.BedrockRuntime", CoreReferences);
        Assert.Contains("Anthropic", CoreReferences);
        Assert.Contains("OpenAI", CoreReferences);
        Assert.DoesNotContain("Anthropic.SDK", CoreReferences);
        Assert.DoesNotContain("Anthropic.Extensions.AI", CoreReferences);
        Assert.DoesNotContain("SemanticKernel", CoreReferences);
    }
}
