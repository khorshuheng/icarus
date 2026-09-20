namespace Icarus.Cli.Ui;

/// <summary>The slash commands the TUI understands (ICARUS-107, pi-style).</summary>
public enum SlashCommandKind
{
    Login,
    Model,
    Provider,
    Effort,
    Workspace,
    Skills,
    Skill,
    Tools,
    Resume,
    Clear,
    Help,
    Exit,
    Unknown,
}

/// <summary>One parsed command: its kind and raw argument (possibly empty).</summary>
public sealed record SlashCommand(SlashCommandKind Kind, string Argument);

/// <summary>Metadata for one command: the source of truth for parsing and completion.</summary>
public sealed record CommandSpec(
    string Name,
    IReadOnlyList<string> Aliases,
    string? ArgumentHint,
    string Description);

/// <summary>The action a submitted input line maps to.</summary>
public abstract record LineAction
{
    /// <summary>A normal user message: a prompt when idle, a steer when busy.</summary>
    public sealed record Message(string Text) : LineAction;

    /// <summary>A slash command.</summary>
    public sealed record Command(SlashCommand Slash) : LineAction;
}

/// <summary>The command table and parser (ICARUS-107).</summary>
public static class SlashCommands
{
    /// <summary>Every command, in display order.</summary>
    public static readonly IReadOnlyList<CommandSpec> All =
    [
        new("login", [], null, "Store a provider API key in the keyring"),
        new("model", [], "<name>", "Change the model (picker when omitted)"),
        new("provider", [], "<name>", "Change the provider (picker when omitted)"),
        new("effort", [], "[level]", "Change thinking effort (picker when omitted)"),
        new("workspace", [], "<path>", "Change the workspace directory"),
        new("skills", [], null, "List discovered skills"),
        new("skill", [], "<name>", "Load a skill's instructions"),
        new("tools", [], null, "List registered tools"),
        new("resume", [], null, "Pick a previous session"),
        new("clear", [], null, "Reset the conversation"),
        new("help", [], null, "Show this list"),
        new("exit", ["quit", "q"], null, "Quit (saving the session)"),
    ];

    /// <summary>Commands whose argument is required for a direct (non-picker) invocation.</summary>
    public static bool RequiresArgument(SlashCommandKind kind) => kind is
        SlashCommandKind.Model or SlashCommandKind.Provider or SlashCommandKind.Skill
        or SlashCommandKind.Workspace;

    /// <summary>Classify a submitted line: a message, or a slash command.</summary>
    public static LineAction Parse(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return new LineAction.Message(trimmed);
        }

        var rest = trimmed[1..];
        var space = rest.IndexOf(' ');
        var name = (space < 0 ? rest : rest[..space]).ToLowerInvariant();
        var argument = space < 0 ? string.Empty : rest[(space + 1)..].Trim();

        var spec = All.FirstOrDefault(s =>
            s.Name == name || s.Aliases.Contains(name, StringComparer.Ordinal));

        return new LineAction.Command(spec is null
            ? new SlashCommand(SlashCommandKind.Unknown, name)
            : new SlashCommand(KindOf(spec.Name), argument));
    }

    /// <summary>Live-filter command names by the token after a leading <c>/</c>.</summary>
    public static IReadOnlyList<CommandSpec> Complete(string input)
    {
        if (!input.StartsWith('/'))
        {
            return [];
        }

        var rest = input[1..];
        if (rest.Contains(' '))
        {
            return [];
        }

        return All.Where(spec => spec.Name.StartsWith(rest, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static SlashCommandKind KindOf(string name) => name switch
    {
        "login" => SlashCommandKind.Login,
        "model" => SlashCommandKind.Model,
        "provider" => SlashCommandKind.Provider,
        "effort" => SlashCommandKind.Effort,
        "workspace" => SlashCommandKind.Workspace,
        "skills" => SlashCommandKind.Skills,
        "skill" => SlashCommandKind.Skill,
        "tools" => SlashCommandKind.Tools,
        "resume" => SlashCommandKind.Resume,
        "clear" => SlashCommandKind.Clear,
        "help" => SlashCommandKind.Help,
        "exit" => SlashCommandKind.Exit,
        _ => SlashCommandKind.Unknown,
    };
}
