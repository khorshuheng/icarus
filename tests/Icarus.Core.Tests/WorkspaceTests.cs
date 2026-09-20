using Icarus.Core.Paths;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tests;

/// <summary>
/// The workspace is a default location, not a sandbox (ICARUS-105, matching
/// CRAB). These tests assert that deliberately, so the behavior stays
/// documented rather than accidental.
/// </summary>
public class WorkspaceTests
{
    [Fact]
    public void Resolves_relative_paths_under_the_root()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hi");
        var ws = Workspace.New(dir.Path);

        Assert.Equal(Path.Combine(ws.Root, "a.txt"), ws.Resolve("a.txt"));
    }

    [Fact]
    public void Allows_dotdot_outside_the_workspace()
    {
        using var dir = new TempDir();
        var ws = Workspace.New(dir.Path);
        var parent = Path.GetDirectoryName(ws.Root)!;

        Assert.Equal(Workspace.New(parent).Root, ws.Resolve("../"));
    }

    [Fact]
    public void Allows_absolute_paths_outside_the_workspace()
    {
        using var dir = new TempDir();
        var ws = Workspace.New(dir.Path);

        Assert.Equal("/etc", ws.Resolve("/etc"));
    }

    [Fact]
    public void Follows_symlinks_outside_the_workspace()
    {
        using var dir = new TempDir();
        using var outside = new TempDir();
        File.WriteAllText(Path.Combine(outside.Path, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(dir.Path, "link"), outside.Path);
        var ws = Workspace.New(dir.Path);

        Assert.Equal(
            Path.Combine(Workspace.New(outside.Path).Root, "secret.txt"),
            ws.Resolve("link/secret.txt"));
    }

    [Fact]
    public void Resolves_a_new_file_under_the_root()
    {
        using var dir = new TempDir();
        var ws = Workspace.New(dir.Path);

        Assert.Equal(Path.Combine(ws.Root, "sub", "deep", "new.txt"), ws.Resolve("sub/deep/new.txt"));
    }

    [Fact]
    public void Normalizes_unicode_spaces_and_the_at_prefix()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a b.txt"), "x");
        var ws = Workspace.New(dir.Path);

        Assert.Equal(Path.Combine(ws.Root, "a b.txt"), ws.Resolve("a\u00A0b.txt"));
        Assert.Equal(Path.Combine(ws.Root, "a b.txt"), ws.Resolve("@a b.txt"));
    }

    [Fact]
    public void Expands_only_a_leading_tilde()
    {
        Assert.Equal("~user/x", Workspace.Normalize("~user/x"));
        Assert.Equal("a/~/b", Workspace.Normalize("a/~/b"));

        var home = IcarusPaths.Home;
        if (home is not null)
        {
            Assert.Equal(Path.Combine(home, "a", "b"), Workspace.Normalize("~/a/b"));
            Assert.Equal(home, Workspace.Normalize("~"));
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "icarus-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}
