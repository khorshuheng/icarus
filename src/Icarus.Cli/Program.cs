using Icarus.Cli.Tui;
using Icarus.Core.Config;
using Icarus.Core.Credentials;
using Icarus.Core.Paths;
using Icarus.Core.Provider;
using Icarus.Core.Providers;
using Icarus.Core.Runtime;
using Icarus.Core.Session;
using Icarus.Core.Tools;
using Icarus.Core.Workspaces;

namespace Icarus.Cli;

/// <summary>
/// The <c>icarus</c> entry point (ICARUS-107): parse flags, load configuration,
/// build the official provider and the four tools, then run the TUI. TUI-only —
/// a non-terminal stdin is an error.
/// </summary>
public static class Program
{
    /// <summary>The version reported by <c>--version</c>.</summary>
    public static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception error) when (error is ConfigException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"icarus: {error.Message}");
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"icarus: {error.Message}");
            return 1;
        }
    }

    internal static int Run(string[] args)
    {
        var flags = Parse(args);

        if (flags.Has("version"))
        {
            Console.WriteLine($"icarus {Version}");
            return 0;
        }

        if (flags.Has("help"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var prompt = flags.Positional.Count > 0 ? string.Join(' ', flags.Positional) : null;

        var configPath = flags.Flag("config")
            ?? (File.Exists(IcarusPaths.ConfigFile) ? IcarusPaths.ConfigFile : null);

        var overrides = new PartialConfig
        {
            Provider = flags.Flag("provider"),
            Model = flags.Flag("model"),
            Region = flags.Flag("region"),
            Workspace = flags.Flag("dir"),
            ThemeName = flags.Flag("theme"),
            MaxIterations = flags.Int("max-iterations"),
        };

        var defaultWorkspace = flags.Flag("dir") ?? Directory.GetCurrentDirectory();
        var config = ConfigLoader.Load(defaultWorkspace, configPath, overrides, apiKey: null);
        config = config with { ApiKey = Credentials.Resolve(config.Provider, flags.Flag("api-key")) };

        var workspace = Workspace.New(config.Workspace);
        var provider = ProviderBuilder.Build(config);
        var tools = ToolSet.Builtins(config);

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            throw new InvalidOperationException(
                "icarus is a full-screen TUI and requires a terminal (stdin/stdout are not a TTY)");
        }

        var sessions = new SessionStore();
        sessions.PruneSessions(workspace.Root, config.SessionRetention);

        var runtime = new AgentRuntime(
            config, provider, tools, workspace, ProviderBuilder.Build, ProviderBuilder.ResolveKey);
        runtime.SetInteractive(true); // a human is present: no iteration cap
        _ = runtime.Start();

        return TuiApp.Run(runtime, sessions, prompt);
    }

    private const string Usage = """
        icarus — a minimal coding agent (TUI)

        Usage: icarus [options] [prompt]

        Options:
          --dir <path>              workspace directory (default: current directory)
          --provider <name>         bedrock | anthropic | fake
          --model <id>              provider model id (required)
          --region <region>         AWS region for Bedrock
          --config <path>           config file (default: ~/.config/icarus/config.toml)
          --theme <dark|light>      TUI theme preset
          --max-iterations <n>      iteration cap for scripted runs
          --api-key <key>           provider API key (else env, else keyring)
          --version                 print the version
          --help                    print this help
        """;

    private sealed class ParsedFlags
    {
        private readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);

        public List<string> Positional { get; } = [];

        public void Set(string key, string? value) => _flags[key] = value ?? string.Empty;

        public bool Has(string key) => _flags.ContainsKey(key);

        public string? Flag(string key) =>
            _flags.TryGetValue(key, out var value) && value is { Length: > 0 } ? value : null;

        public int? Int(string key) =>
            Flag(key) is { } value && int.TryParse(value, out var number) ? number : null;
    }

    private static ParsedFlags Parse(string[] args)
    {
        var parsed = new ParsedFlags();
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (!argument.StartsWith("--"))
            {
                parsed.Positional.Add(argument);
                continue;
            }

            var name = argument[2..];
            if (name.Contains('='))
            {
                var separator = name.IndexOf('=');
                parsed.Set(name[..separator], name[(separator + 1)..]);
                continue;
            }

            // Boolean flags take no value.
            if (name is "version" or "help")
            {
                parsed.Set(name, string.Empty);
                continue;
            }

            var value = i + 1 < args.Length ? args[++i] : string.Empty;
            parsed.Set(name, value);
        }

        return parsed;
    }
}
