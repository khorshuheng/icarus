using System.Diagnostics;
using System.Text.Json.Nodes;
using Icarus.Core.Config;
using Icarus.Core.Tools;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tests;

internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Dir = Path.Combine(Path.GetTempPath(), "icarus-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Workspace = Workspace.New(Dir);
    }

    public string Dir { get; }

    public Workspace Workspace { get; }

    public string Write(string relative, string content)
    {
        var path = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}

public class ReadToolTests
{
    private static readonly ReadTool Tool = new(32_000);

    private static JsonObject Args(string path, params (string Key, JsonNode Value)[] extra)
    {
        var args = new JsonObject { ["path"] = path };
        foreach (var (key, value) in extra)
        {
            args[key] = value;
        }

        return args;
    }

    [Fact]
    public async Task Reads_a_file_by_line_range()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "l1\nl2\nl3\nl4\nl5\n");

        var output = await Tool.RunAsync(ws.Workspace, Args("a.txt", ("offset", 2), ("limit", 2)), CancellationToken.None);

        Assert.StartsWith("l2\nl3", output.Content);
        Assert.Contains("Use offset=4 to continue", output.Content);
    }

    [Fact]
    public async Task Reads_multiple_paths_with_headers()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "alpha");
        ws.Write("b.txt", "beta");

        var args = new JsonObject { ["path"] = new JsonArray("a.txt", "b.txt") };

        var output = await Tool.RunAsync(ws.Workspace, args, CancellationToken.None);

        Assert.Contains("==> a.txt <==", output.Content);
        Assert.Contains("==> b.txt <==", output.Content);
        Assert.Contains("alpha", output.Content);
        Assert.Contains("beta", output.Content);
    }

    [Fact]
    public async Task Reads_a_directory_with_globs_and_exclusions()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/a.rs", "one");
        ws.Write("src/deep/b.rs", "two");
        ws.Write("src/c.txt", "skip");
        ws.Write("src/vendor/d.rs", "excluded");

        var output = await Tool.RunAsync(
            ws.Workspace,
            Args("src", ("glob", new JsonArray("**/*.rs", "!vendor/**"))),
            CancellationToken.None);

        Assert.Contains("a.rs", output.Content);
        Assert.Contains("deep/b.rs", output.Content);
        Assert.DoesNotContain("c.txt", output.Content);
        Assert.DoesNotContain("vendor", output.Content);
    }

    [Fact]
    public async Task Skips_hidden_and_gitignored_files()
    {
        using var ws = new TempWorkspace();
        ws.Write("keep.rs", "keep");
        ws.Write(".hidden.rs", "hidden");
        ws.Write("build/ignored.rs", "ignored");
        ws.Write(".gitignore", "build/\n");

        var output = await Tool.RunAsync(
            ws.Workspace, Args(".", ("glob", "**/*.rs")), CancellationToken.None);

        Assert.Contains("keep", output.Content);
        Assert.DoesNotContain("hidden", output.Content);
        Assert.DoesNotContain("ignored", output.Content);
    }

    [Fact]
    public async Task Caps_the_number_of_files()
    {
        using var ws = new TempWorkspace();
        for (var i = 0; i < ReadTool.MaxFiles + 5; i++)
        {
            ws.Write($"f{i:D3}.txt", "x");
        }

        var output = await Tool.RunAsync(
            ws.Workspace, Args(".", ("glob", "*.txt")), CancellationToken.None);

        Assert.Contains($"[stopped after {ReadTool.MaxFiles} files", output.Content);
    }

    [Fact]
    public async Task Requiring_a_glob_for_a_directory_is_an_error()
    {
        using var ws = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(ws.Dir, "src"));

        await Assert.ThrowsAsync<ToolInvalidException>(() =>
            Tool.RunAsync(ws.Workspace, Args("src"), CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_file_is_a_not_found_error()
    {
        using var ws = new TempWorkspace();

        await Assert.ThrowsAsync<ToolNotFoundException>(() =>
            Tool.RunAsync(ws.Workspace, Args("nope.txt"), CancellationToken.None));
    }
}

public class WriteToolTests
{
    private static readonly WriteTool Tool = new();

    [Fact]
    public async Task Creates_a_file_and_its_parent_directories()
    {
        using var ws = new TempWorkspace();
        var args = new JsonObject { ["path"] = "sub/deep/new.txt", ["content"] = "hello" };

        await Tool.RunAsync(ws.Workspace, args, CancellationToken.None);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(ws.Dir, "sub", "deep", "new.txt")));
    }

    [Fact]
    public async Task Refuses_to_write_over_a_directory()
    {
        using var ws = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(ws.Dir, "adir"));
        var args = new JsonObject { ["path"] = "adir", ["content"] = "x" };

        await Assert.ThrowsAsync<ToolInvalidException>(() =>
            Tool.RunAsync(ws.Workspace, args, CancellationToken.None));
    }
}

public class BashToolTests
{
    [Fact]
    public async Task Runs_a_command_and_returns_its_output()
    {
        using var ws = new TempWorkspace();
        var tool = new BashTool(32_000, 120);

        var output = await tool.RunAsync(
            ws.Workspace, new JsonObject { ["command"] = "echo hello" }, CancellationToken.None);

        Assert.Equal("hello", output.Content);
    }

    [Fact]
    public async Task A_non_zero_exit_is_a_tool_error()
    {
        using var ws = new TempWorkspace();
        var tool = new BashTool(32_000, 120);

        var error = await Assert.ThrowsAsync<ToolCommandException>(() => tool.RunAsync(
            ws.Workspace, new JsonObject { ["command"] = "echo boom; exit 3" }, CancellationToken.None));

        Assert.Contains("exited with code 3", error.Message);
    }

    [Fact]
    public async Task A_timeout_kills_the_command()
    {
        using var ws = new TempWorkspace();
        var tool = new BashTool(32_000, 120);

        await Assert.ThrowsAsync<ToolTimeoutException>(() => tool.RunAsync(
            ws.Workspace,
            new JsonObject { ["command"] = "sleep 30", ["timeout"] = 1 },
            CancellationToken.None));
    }

    [Fact]
    public async Task A_backgrounded_pipe_holder_does_not_hang_the_tool()
    {
        using var ws = new TempWorkspace();
        var tool = new BashTool(32_000, 120);
        var stopwatch = Stopwatch.StartNew();

        var output = await tool.RunAsync(
            ws.Workspace,
            new JsonObject { ["command"] = "sleep 30 & echo done" },
            CancellationToken.None);

        Assert.Contains("done", output.Content);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Cancellation_stops_the_command()
    {
        using var ws = new TempWorkspace();
        var tool = new BashTool(32_000, 120);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAsync<ToolCancelledException>(() => tool.RunAsync(
            ws.Workspace, new JsonObject { ["command"] = "sleep 30" }, cts.Token));
    }
}

public class EditToolTests
{
    private static readonly EditTool Tool = new();

    private static JsonObject Single(string oldText, string newText) => new()
    {
        ["path"] = "a.txt",
        ["oldText"] = oldText,
        ["newText"] = newText,
    };

    [Fact]
    public async Task Replaces_a_single_passage()
    {
        using var ws = new TempWorkspace();
        var path = ws.Write("a.txt", "hello world\n");

        await Tool.RunAsync(ws.Workspace, Single("world", "there"), CancellationToken.None);

        Assert.Equal("hello there\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Applies_a_batch_of_disjoint_edits()
    {
        using var ws = new TempWorkspace();
        var path = ws.Write("a.txt", "one two three\n");
        var args = new JsonObject
        {
            ["path"] = "a.txt",
            ["edits"] = new JsonArray(
                new JsonObject { ["oldText"] = "one", ["newText"] = "1" },
                new JsonObject { ["oldText"] = "three", ["newText"] = "3" }),
        };

        await Tool.RunAsync(ws.Workspace, args, CancellationToken.None);

        Assert.Equal("1 two 3\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Matching_is_fuzzy_across_confusables()
    {
        using var ws = new TempWorkspace();
        var path = ws.Write("a.txt", "He said \u201Chi\u201D \u2014 loudly.\n");

        await Tool.RunAsync(ws.Workspace, Single("He said \"hi\" - loudly.", "changed"), CancellationToken.None);

        Assert.Equal("changed\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Detects_a_duplicate_match()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "x\nx\n");

        await Assert.ThrowsAsync<ToolInvalidException>(() =>
            Tool.RunAsync(ws.Workspace, Single("x", "y"), CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_match_is_an_error()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "abc\n");

        await Assert.ThrowsAsync<ToolInvalidException>(() =>
            Tool.RunAsync(ws.Workspace, Single("zzz", "y"), CancellationToken.None));
    }

    [Fact]
    public async Task Preserves_a_utf8_bom()
    {
        using var ws = new TempWorkspace();
        var path = Path.Combine(ws.Dir, "bom.txt");
        File.WriteAllText(path, "a\nb\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await Tool.RunAsync(
            ws.Workspace,
            new JsonObject { ["path"] = "bom.txt", ["oldText"] = "b", ["newText"] = "c" },
            CancellationToken.None);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("a\nc\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Preserves_crlf_line_endings()
    {
        using var ws = new TempWorkspace();
        var path = ws.Write("a.txt", "a\r\nb\r\n");

        await Tool.RunAsync(ws.Workspace, Single("b", "c"), CancellationToken.None);

        Assert.Equal("a\r\nc\r\n", File.ReadAllText(path));
    }
}

public class ToolSetTests
{
    private static ToolSet Builtins(TempWorkspace ws) => ToolSet.Builtins(ConfigLoader.Resolve(
        ws.Dir, new PartialConfig { Provider = "fake", Model = "m" }, new PartialConfig(), null));

    [Fact]
    public void Lists_exactly_the_four_builtins()
    {
        using var ws = new TempWorkspace();

        var names = Builtins(ws).Listing.Select(t => t.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["bash", "edit", "read", "write"], names);
    }

    [Fact]
    public async Task Rejects_an_unknown_tool()
    {
        using var ws = new TempWorkspace();

        await Assert.ThrowsAsync<ToolArgumentException>(() => Builtins(ws).ExecuteAsync(
            ws.Workspace, "search", new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_arguments_that_violate_the_schema()
    {
        using var ws = new TempWorkspace();

        await Assert.ThrowsAsync<ToolArgumentException>(() => Builtins(ws).ExecuteAsync(
            ws.Workspace, "read", new JsonObject(), CancellationToken.None));
    }
}
