namespace Icarus.Cli;

/// <summary>
/// The <c>icarus</c> entry point. The TUI arrives with ICARUS-107; until then
/// this is a minimal placeholder so the executable and its tests exist.
/// </summary>
public static class Program
{
    /// <summary>The version reported by <c>--version</c>.</summary>
    public static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Run the CLI. Returns a process exit code.</summary>
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--version" or "-v")
        {
            Console.WriteLine($"icarus {Version}");
            return 0;
        }

        Console.Error.WriteLine("icarus: the TUI is not implemented yet (ICARUS-107).");
        return 1;
    }
}
