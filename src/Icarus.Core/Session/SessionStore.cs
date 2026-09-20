using System.Text;
using System.Text.Json;
using Icarus.Core.Paths;
using Icarus.Core.Provider;
using Icarus.Core.Serialization;

namespace Icarus.Core.Session;

/// <summary>
/// Append-only JSONL session persistence (ICARUS-106). Sessions are keyed by
/// working directory under <c>&lt;data&gt;/icarus/sessions/&lt;cwd-encoded&gt;/</c>;
/// writes are atomic (temp + rename) and a torn final line is dropped and
/// repaired on load. There is deliberately no database.
/// </summary>
public sealed class SessionStore
{
    /// <summary>The on-disk format version.</summary>
    public const int FormatVersion = 1;

    private readonly string _root;

    public SessionStore(string? root = null) => _root = root ?? IcarusPaths.SessionRoot;

    /// <summary>The session root directory.</summary>
    public string Root => _root;

    /// <summary>Persist <paramref name="history"/> as a new session for <paramref name="cwd"/>.</summary>
    public string Save(string cwd, IReadOnlyList<Message> history)
    {
        var directory = DirectoryFor(cwd);
        Directory.CreateDirectory(directory);

        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, id + ".jsonl");
        var createdAt = DateTimeOffset.UtcNow;

        var builder = new StringBuilder();
        Append(builder, new HeaderEntry(FormatVersion, id, createdAt, cwd, ParentSessionId: null));
        foreach (var message in history)
        {
            Append(builder, ToEntry(message));
        }

        WriteAtomic(path, builder.ToString());
        return path;
    }

    /// <summary>Load the most recent session for <paramref name="cwd"/>, or <c>null</c>.</summary>
    public IReadOnlyList<Message>? LoadPrevious(string cwd)
    {
        var newest = ListSessions(cwd).FirstOrDefault();
        return newest is null ? null : LoadAt(newest.Path);
    }

    /// <summary>Load a specific session file.</summary>
    public IReadOnlyList<Message> LoadAt(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new SessionException("missing session header");
        }

        var header = ReadHeader(lines[0]);
        if (header.Version > FormatVersion)
        {
            throw new SessionException(
                $"session format version {header.Version} is newer than supported ({FormatVersion})");
        }

        var messages = new List<Message>();
        var validLines = 1;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                validLines = i + 1;
                continue;
            }

            SessionEntry? entry;
            try
            {
                entry = IcarusJson.Deserialize<SessionEntry>(line);
                if (entry is HeaderEntry)
                {
                    throw new SessionException($"unexpected header line at {i + 1}");
                }
            }
            catch (JsonException)
            {
                if (i == lines.Length - 1)
                {
                    Repair(path, validLines);
                    return messages;
                }

                throw new SessionException($"corrupt session at line {i + 1}");
            }

            if (entry is null)
            {
                throw new SessionException($"corrupt session at line {i + 1}");
            }

            messages.Add(ToMessage(entry));
            validLines = i + 1;
        }

        return messages;
    }

    /// <summary>Every session for <paramref name="cwd"/>, newest first.</summary>
    public IReadOnlyList<SessionSummary> ListSessions(string cwd)
    {
        var directory = DirectoryFor(cwd);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var summaries = new List<SessionSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            try
            {
                var first = File.ReadLines(path).FirstOrDefault();
                if (first is null)
                {
                    continue;
                }

                var header = ReadHeader(first);
                var lines = File.ReadLines(path).Count();
                summaries.Add(new SessionSummary(header.Id, path, header.CreatedAt, Math.Max(0, lines - 1)));
            }
            catch (Exception e) when (e is JsonException or IOException or SessionException)
            {
                // Skip unreadable sessions rather than failing the listing.
            }
        }

        return summaries.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToArray();
    }

    /// <summary>Delete the most recent session for <paramref name="cwd"/>.</summary>
    public void ClearPrevious(string cwd)
    {
        var newest = ListSessions(cwd).FirstOrDefault();
        if (newest is not null)
        {
            File.Delete(newest.Path);
        }
    }

    /// <summary>Keep only the newest <paramref name="keep"/> sessions; <c>0</c> disables pruning.</summary>
    public int PruneSessions(string cwd, int keep)
    {
        if (keep <= 0)
        {
            return 0;
        }

        var sessions = ListSessions(cwd);
        var removed = 0;
        foreach (var session in sessions.Skip(keep))
        {
            File.Delete(session.Path);
            removed++;
        }

        return removed;
    }

    /// <summary>The session id encoded in a file name.</summary>
    public static string? FileId(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>The directory that holds a working directory's sessions.</summary>
    public string DirectoryFor(string cwd) => Path.Combine(_root, Uri.EscapeDataString(cwd));

    private static void Append(StringBuilder builder, SessionEntry entry) =>
        builder.Append(IcarusJson.Serialize<SessionEntry>(entry)).Append('\n');

    private static HeaderEntry ReadHeader(string line)
    {
        try
        {
            return IcarusJson.Deserialize<SessionEntry>(line) as HeaderEntry
                ?? throw new SessionException("first line is not a session header");
        }
        catch (JsonException e)
        {
            throw new SessionException($"invalid session header: {e.Message}");
        }
    }

    private static void Repair(string path, int validLines)
    {
        var prefix = File.ReadLines(path).Take(validLines);
        var text = string.Join('\n', prefix) + "\n";
        WriteAtomic(path, text);
    }

    private static void WriteAtomic(string path, string contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static SessionEntry ToEntry(Message message) => message switch
    {
        Message.System system => new SystemEntry(system.Text),
        Message.User user => new UserEntry(user.Text),
        Message.Assistant assistant => new AssistantEntry(assistant.Text, assistant.ToolCalls),
        Message.ToolResult result => new ToolEntry(result.ToolCallId, result.Result),
        _ => throw new SessionException($"unsupported message type {message.GetType().Name}"),
    };

    private static Message ToMessage(SessionEntry entry) => entry switch
    {
        SystemEntry system => new Message.System(system.Text),
        UserEntry user => new Message.User(user.Text),
        AssistantEntry assistant => new Message.Assistant(assistant.Text, assistant.ToolCalls),
        ToolEntry tool => new Message.ToolResult(tool.ToolCallId, tool.Result),
        _ => throw new SessionException($"unsupported session entry {entry.GetType().Name}"),
    };
}
