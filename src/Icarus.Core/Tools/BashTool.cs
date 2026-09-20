using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Icarus.Core.Workspaces;

namespace Icarus.Core.Tools;

/// <summary>
/// The <c>bash</c> tool: run a shell command in the workspace (ICARUS-104).
/// Prefers <c>/bin/bash</c> over <c>/bin/sh</c>, runs with a default timeout,
/// captures stdout and stderr, and kills the whole process tree on
/// timeout/cancel so a backgrounded pipe-holder cannot hang the tool.
/// </summary>
public sealed class BashTool(int maxOutputBytes, int? defaultTimeoutSecs) : ITool
{
    /// <summary>Upper bound for a model-supplied timeout, matching CRAB's i32 seconds.</summary>
    public const int MaxTimeoutSecs = int.MaxValue;

    private const int PipeGraceMs = 500;
    private const long MaxTimerMs = 4_000_000_000L;

    public string Name => "bash";

    public string Description => "Run a shell command in the workspace and return its output.";

    public JsonNode Schema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Shell command to run in the workspace.",
            },
            ["timeout"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = MaxTimeoutSecs,
                ["description"] = "Timeout in seconds (optional; the default applies otherwise).",
            },
        },
        ["required"] = new JsonArray("command"),
    };

    public async Task<ToolOutput> RunAsync(Workspace workspace, JsonNode args, CancellationToken cancellationToken)
    {
        var command = ToolArgs.RequiredString(args, "command");
        var timeout = ToolArgs.OptionalInt(args, "timeout", min: 1, max: MaxTimeoutSecs)
            ?? defaultTimeoutSecs;

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveShell(),
            WorkingDirectory = workspace.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo };
        var buffer = new StringBuilder();
        var gate = new Lock();

        process.Start();
        var stdout = Pump(process.StandardOutput);
        var stderr = Pump(process.StandardError);

        var timedOut = false;
        using var timeoutSource = new CancellationTokenSource();
        if (timeout is { } seconds and > 0)
        {
            timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(
                Math.Min((long)seconds * 1000, MaxTimerMs)));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        // Grace period for the stream readers: a descendant holding the pipe
        // must not hang the tool (ICARUS-104 / CRAB-147).
        await Task.WhenAny(
            Task.WhenAll(stdout, stderr),
            Task.Delay(PipeGraceMs, CancellationToken.None));

        if (cancellationToken.IsCancellationRequested)
        {
            throw new ToolCancelledException();
        }

        var full = buffer.ToString();
        var content = FormatOutput(full);

        if (timedOut)
        {
            throw new ToolTimeoutException($"{command}\n\n[timed out after {timeout}s]\n{content}");
        }

        if (process.ExitCode != 0)
        {
            throw new ToolCommandException(
                $"command exited with code {process.ExitCode}\n{content}");
        }

        return new ToolOutput(content.Length == 0 ? "(no output)" : content);

        Task Pump(StreamReader reader) => Task.Run(async () =>
        {
            var chunk = new char[4096];
            while (true)
            {
                int read;
                try
                {
                    read = await reader.ReadAsync(chunk.AsMemory(), CancellationToken.None);
                }
                catch (IOException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                lock (gate)
                {
                    buffer.Append(chunk, 0, read);
                }
            }
        }, CancellationToken.None);
    }

    /// <summary>Prefer <c>/bin/bash</c>, then <c>bash</c> on PATH, then <c>/bin/sh</c>.</summary>
    public static string ResolveShell()
    {
        if (File.Exists("/bin/bash"))
        {
            return "/bin/bash";
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, "bash");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return "/bin/sh";
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already exited
        }
    }

    private string FormatOutput(string full)
    {
        if (full.Length <= maxOutputBytes)
        {
            return full.TrimEnd('\n');
        }

        var tail = TextUtil.Tail(full, maxOutputBytes);
        var path = WriteTempFile(full);
        return $"[output truncated; full output saved to {path}]\n…{tail}";
    }

    private static string WriteTempFile(string contents)
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"icarus-bash-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, contents);
        return path;
    }
}
