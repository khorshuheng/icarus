using System.Text;
using Icarus.Cli.Ui;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;
using Icarus.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Icarus.Cli.Tui;

/// <summary>
/// The Terminal.Gui shell (ICARUS-107): a client of <see cref="AgentRuntime"/>
/// that renders the terminal-free <see cref="UiModel"/>. It never owns the
/// agent loop. Editing and scrolling use Terminal.Gui's TextView/TextField for
/// native behaviour; the pure models remain the tested source of truth for
/// parsing, event folding and scroll semantics.
/// </summary>
public static class TuiApp
{
    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static int Run(AgentRuntime runtime, SessionStore sessions, string? initialPrompt)
    {
        var model = new UiModel(runtime.State);
        var spinnerFrame = 0;
        string? savedPath = null;

        Application.Init();
        try
        {
            var transcript = new TextView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2),
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = true,
            };

            var input = new TextField
            {
                X = 0,
                Y = Pos.AnchorEnd(2),
                Width = Dim.Fill(),
            };

            var footer = new Label
            {
                X = 0,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill(),
            };

            var window = new Window
            {
                Title = "icarus",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            window.Add(transcript);
            window.Add(input);
            window.Add(footer);

            void Refresh()
            {
                var builder = new StringBuilder();
                foreach (var line in model.Render())
                {
                    builder.Append(line.Text).Append('\n');
                }

                transcript.Text = builder.ToString();
                transcript.MoveEnd();
                transcript.SetNeedsDraw();

                var spinner = model.Busy ? Spinner[spinnerFrame % Spinner.Length] + " " : string.Empty;
                var tokens = model.PromptTokens is { } count ? count.ToString() : "-";
                footer.Text =
                    $" {spinner}{model.State.Provider}/{model.State.Model} · effort {model.State.Effort.Name()} "
                    + $"· {model.State.Workspace} · tokens {tokens} · turns {model.Turns}";
                footer.SetNeedsDraw();
            }

            void Submit()
            {
                var line = (input.Value ?? string.Empty).Trim();
                input.Value = string.Empty;
                if (line.Length == 0)
                {
                    return;
                }

                switch (SlashCommands.Parse(line))
                {
                    case LineAction.Message message when model.Busy:
                        model.PushUser(message.Text);
                        model.PushNotice("(steer queued)");
                        runtime.Steer(message.Text);
                        break;
                    case LineAction.Message message:
                        model.PushUser(message.Text);
                        runtime.Prompt(message.Text);
                        break;
                    case LineAction.Command command:
                        HandleCommand(command.Slash);
                        break;
                }

                Refresh();
            }

            void HandleCommand(SlashCommand command)
            {
                switch (command.Kind)
                {
                    case SlashCommandKind.Help:
                        model.PushNotice(HelpText());
                        break;
                    case SlashCommandKind.Clear:
                        runtime.Clear();
                        model.LoadHistory([]);
                        model.PushNotice("(cleared)");
                        break;
                    case SlashCommandKind.Exit:
                        Application.RequestStop(window);
                        break;
                    case SlashCommandKind.Tools:
                        model.PushNotice(string.Join(
                            "  ", runtime.ToolListing.Select(t => t.Name)));
                        break;
                    case SlashCommandKind.Resume:
                        Resume();
                        break;
                    case SlashCommandKind.Model when command.Argument.Length > 0:
                        runtime.SetModel(command.Argument);
                        break;
                    case SlashCommandKind.Provider when command.Argument.Length > 0:
                        runtime.SetProvider(command.Argument);
                        break;
                    case SlashCommandKind.Effort when command.Argument.Length > 0:
                        if (EffortExtensions.Parse(command.Argument) is { } effort)
                        {
                            runtime.SetEffort(effort);
                        }
                        else
                        {
                            model.PushNotice($"unknown effort '{command.Argument}' (off|minimal|low|medium|high)");
                        }

                        break;
                    case SlashCommandKind.Workspace when command.Argument.Length > 0:
                        runtime.SwitchWorkspace(command.Argument);
                        break;
                    case SlashCommandKind.Unknown:
                        model.PushNotice($"unknown command '/{command.Argument}' — /help lists commands");
                        break;
                    default:
                        model.PushNotice($"usage: /{command.Kind.ToString().ToLowerInvariant()} {ArgumentHint(command.Kind)}");
                        break;
                }
            }

            void Resume()
            {
                var summaries = sessions.ListSessions(runtime.WorkspaceRoot);
                if (summaries.Count == 0)
                {
                    model.PushNotice("no previous session for this workspace");
                    return;
                }

                var history = sessions.LoadAt(summaries[0].Path);
                runtime.ReplaceHistory(history);
                model.LoadHistory(history);
                model.PushNotice($"resumed session {summaries[0].Id}");
            }

            Application.KeyDown += (_, key) =>
            {
                switch (key.KeyCode)
                {
                    case KeyCode.Esc:
                        if (model.Busy)
                        {
                            runtime.Abort();
                        }
                        else
                        {
                            Application.RequestStop(window);
                        }

                        key.Handled = true;
                        break;
                    case KeyCode.C when key.IsCtrl:
                        if (model.Busy)
                        {
                            runtime.Abort();
                        }
                        else
                        {
                            Application.RequestStop(window);
                        }

                        key.Handled = true;
                        break;
                }
            };

            input.Accepted += (_, _) => Submit();

            Application.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                spinnerFrame++;
                if (model.Busy)
                {
                    footer.SetNeedsDraw();
                }

                return true;
            });

            _ = Task.Run(async () =>
            {
                await foreach (var @event in runtime.Events.ReadAllAsync())
                {
                    Application.Invoke(() =>
                    {
                        model.Apply(@event);
                        Refresh();
                    });
                }
            });

            Refresh();
            if (!string.IsNullOrWhiteSpace(initialPrompt))
            {
                model.PushUser(initialPrompt!);
                runtime.Prompt(initialPrompt!);
            }

            Application.Run(window, _ => true);

            if (runtime.History.Any(message => message is Message.Assistant))
            {
                savedPath = sessions.Save(runtime.WorkspaceRoot, runtime.History);
            }
        }
        finally
        {
            runtime.Shutdown();
            Application.Shutdown();
        }

        // Write only after the TUI has released the screen.
        if (savedPath is not null)
        {
            Console.Error.WriteLine($"session saved: {savedPath}");
        }

        return 0;
    }

    private static string HelpText() =>
        "commands: " + string.Join(", ", SlashCommands.All.Select(s => "/" + s.Name));

    private static string ArgumentHint(SlashCommandKind kind) => kind switch
    {
        SlashCommandKind.Model => "<name>",
        SlashCommandKind.Provider => "<name>",
        SlashCommandKind.Effort => "[level]",
        SlashCommandKind.Skill => "<name>",
        SlashCommandKind.Workspace => "<path>",
        _ => string.Empty,
    };
}
