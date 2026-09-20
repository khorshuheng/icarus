using System.Text.Json.Nodes;
using Icarus.Core.Provider;
using Icarus.Core.Session;

namespace Icarus.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "icarus-session-" + Guid.NewGuid().ToString("N"));

    private SessionStore Store => new(_root);

    private static IReadOnlyList<Message> Sample() =>
    [
        new Message.System("seed prompt"),
        new Message.User("hi"),
        Message.Assistant.OfToolCalls([new ToolCall("c1", "read", new JsonObject { ["path"] = "a.txt" })]),
        new Message.ToolResult("c1", "file contents"),
        Message.Assistant.OfText("done"),
    ];

    [Fact]
    public void Round_trips_a_multi_turn_session()
    {
        var path = Store.Save("/work/proj", Sample());

        var loaded = Store.LoadAt(path);

        Assert.Equal(5, loaded.Count);
        Assert.Equal("seed prompt", Assert.IsType<Message.System>(loaded[0]).Text);
        Assert.Equal("hi", Assert.IsType<Message.User>(loaded[1]).Text);

        var assistant = Assert.IsType<Message.Assistant>(loaded[2]);
        Assert.Null(assistant.Text);
        var call = Assert.Single(assistant.ToolCalls);
        Assert.Equal("c1", call.Id);
        Assert.Equal("read", call.Name);
        Assert.Equal("a.txt", call.Args["path"]!.GetValue<string>());

        Assert.Equal("file contents", Assert.IsType<Message.ToolResult>(loaded[3]).Result);
        Assert.Equal("done", Assert.IsType<Message.Assistant>(loaded[4]).Text);
    }

    [Fact]
    public void A_torn_final_line_is_dropped_and_repaired()
    {
        var path = Store.Save("/work/proj", Sample());
        File.AppendAllText(path, "{\"kind\":\"user\",\"text\":\"half");

        var loaded = Store.LoadAt(path);

        Assert.Equal(5, loaded.Count);
        Assert.DoesNotContain("half", File.ReadAllText(path));
    }

    [Fact]
    public void List_and_load_previous_pick_the_newest()
    {
        Store.Save("/work/proj", Sample());
        Thread.Sleep(5);
        var newest = Store.Save("/work/proj", [new Message.System("seed"), new Message.User("second")]);

        var sessions = Store.ListSessions("/work/proj");
        var previous = Store.LoadPrevious("/work/proj");

        Assert.Equal(2, sessions.Count);
        Assert.Equal(SessionStore.FileId(newest), sessions[0].Id);
        Assert.NotNull(previous);
        Assert.Contains(previous!, m => m is Message.User { Text: "second" });
    }

    [Fact]
    public void Sessions_are_isolated_by_working_directory()
    {
        Store.Save("/work/a", Sample());
        Store.Save("/work/b", Sample());

        Assert.Single(Store.ListSessions("/work/a"));
        Assert.Single(Store.ListSessions("/work/b"));
        Assert.Empty(Store.ListSessions("/work/c"));
    }

    [Fact]
    public void Clear_previous_removes_the_newest_session()
    {
        Store.Save("/work/proj", Sample());
        Thread.Sleep(5);
        Store.Save("/work/proj", Sample());
        Assert.Equal(2, Store.ListSessions("/work/proj").Count);

        Store.ClearPrevious("/work/proj");

        Assert.Single(Store.ListSessions("/work/proj"));
    }

    [Fact]
    public void Prune_keeps_only_the_newest_n()
    {
        for (var i = 0; i < 4; i++)
        {
            Store.Save("/work/proj", Sample());
            Thread.Sleep(3);
        }

        var removed = Store.PruneSessions("/work/proj", keep: 2);

        Assert.Equal(2, removed);
        Assert.Equal(2, Store.ListSessions("/work/proj").Count);
    }

    [Fact]
    public void Prune_zero_disables_pruning()
    {
        Store.Save("/work/proj", Sample());

        Assert.Equal(0, Store.PruneSessions("/work/proj", keep: 0));
        Assert.Single(Store.ListSessions("/work/proj"));
    }

    [Fact]
    public void A_rejected_version_is_fatal()
    {
        var path = Store.Save("/work/proj", Sample());
        var text = File.ReadAllText(path).Replace("\"version\":1", "\"version\":99");
        File.WriteAllText(path, text);

        Assert.Throws<SessionException>(() => Store.LoadAt(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
