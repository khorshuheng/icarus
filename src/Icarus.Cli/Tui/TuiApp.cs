using System.Collections.Concurrent;
using Icarus.Cli.Ui;
using Icarus.Core.Credentials;
using Icarus.Core.Provider;
using Icarus.Core.Runtime;
using Icarus.Core.Session;
using Icarus.Core.Theme;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using CredentialResolver = Icarus.Core.Credentials.Credentials;
using ThemeType = Icarus.Core.Theme.Theme;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Icarus.Cli.Tui;

/// <summary>
/// The Terminal.Gui shell (ICARUS-107): a client of <see cref="AgentRuntime"/>
/// that renders the terminal-free <see cref="UiModel"/>. It never owns the
/// agent loop. The transcript is a read-only <c>TextView</c> (attributed cells
/// per role); the input is a <c>TextField</c>; slash completion and the command
/// pickers use <see cref="ListOverlay"/>, an overlay the shell draws itself.
/// </summary>
public static class TuiApp
{
    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private static readonly string[] EffortLevels = ["off", "minimal", "low", "medium", "high"];

    public static int Run(AgentRuntime runtime, SessionStore sessions, string? initialPrompt, ThemeType theme)
    {
        var model = new UiModel(runtime.State);
        var spinnerFrame = 0;
        string? savedPath = null;
        var loginMode = false;
        var modelPickerPending = false;
        var dropdownMatches = new List<CommandSpec>();
        Action<int>? overlayApply = null;
        var overlayModal = false;
        var lastInput = string.Empty;
        var refocusPending = false;

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
                CanFocus = false, // never steal focus from the input line
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

            var overlay = new ListOverlay();

            var window = new Window
            {
                Title = "icarus",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };

            // ICARUS-108: apply the resolved theme so the UI is not left on
            // Terminal.Gui's low-contrast default scheme.
            var scheme = ThemeMap.ToScheme(theme);
            window.SetScheme(scheme);
            transcript.SetScheme(scheme);
            input.SetScheme(scheme);
            footer.SetScheme(scheme);
            overlay.Configure(
                ThemeMap.ToAttribute(theme[ThemeToken.Assistant]),
                ThemeMap.ToAttribute(theme[ThemeToken.Selection]),
                ThemeMap.ToAttribute(theme[ThemeToken.Border]),
                ThemeMap.ToAttribute(theme[ThemeToken.Title]));

            window.Add(transcript);
            window.Add(input);
            window.Add(footer);
            window.Add(overlay);
            FocusInput();

            void FocusInput()
            {
                input.Enabled = true;
                input.Secret = loginMode;
                window.SetFocus();
                input.SetFocus();
                window.SetNeedsDraw();
            }

            void Refresh()
            {
                var width = transcript.Viewport.Width > 20 ? transcript.Viewport.Width : 80;

                // ICARUS-108/139: attributed cells so each role (user, assistant,
                // thinking, tool, notice) is visually distinct, while retaining
                // TextView wrapping, scrolling and selection.
                var rows = new List<List<Cell>>();
                foreach (var line in model.Render(width))
                {
                    var attribute = RoleAttribute(line.Role);
                    foreach (var physical in line.Text.Split('\n'))
                    {
                        var cells = new List<Cell>(physical.Length);
                        foreach (var rune in physical.EnumerateRunes())
                        {
                            cells.Add(new Cell(attribute, false, rune.ToString()));
                        }

                        rows.Add(cells);
                    }
                }

                transcript.Load(rows);
                transcript.MoveEnd();
                transcript.SetNeedsDraw();

                var spinner = model.Busy ? Spinner[spinnerFrame % Spinner.Length] + " " : string.Empty;
                var tokens = model.PromptTokens is { } count ? count.ToString() : "-";
                footer.Text =
                    $" {spinner}{model.State.Provider}/{model.State.Model} · effort {model.State.Effort.Name()} "
                    + $"· {model.State.Workspace} · tokens {tokens} · turns {model.Turns}";
                footer.SetNeedsDraw();
            }

            TgAttribute RoleAttribute(TranscriptRole role) => ThemeMap.ToAttribute(theme[role switch
            {
                TranscriptRole.User => ThemeToken.User,
                TranscriptRole.Assistant => ThemeToken.Assistant,
                TranscriptRole.Thinking => ThemeToken.Thinking,
                TranscriptRole.Tool => ThemeToken.Tool,
                _ => ThemeToken.Notice,
            }]);

            void OpenList(string title, IReadOnlyList<string> items, Action<int> apply, bool modal)
            {
                if (items.Count == 0)
                {
                    overlay.Close();
                    return;
                }

                overlayApply = apply;
                overlayModal = modal;
                overlay.Open(title, items, modal);
                if (modal)
                {
                    input.Enabled = false;
                }
            }

            void CloseOverlay()
            {
                overlay.Close();
                overlayApply = null;
                overlayModal = false;
                FocusInput();
                refocusPending = true; // re-apply on the UI loop, not mid-key-event
            }

            void AcceptOverlay()
            {
                var index = overlay.Selected;
                var apply = overlayApply;
                CloseOverlay();
                apply?.Invoke(index);
                lastInput = input.Value ?? string.Empty;
                UpdateCompletions();
                Refresh();
            }

            void UpdateCompletions()
            {
                if (overlayModal || loginMode)
                {
                    return;
                }

                var value = input.Value ?? string.Empty;
                if (value.StartsWith('/') && !value.Contains(' '))
                {
                    var matches = SlashCommands.Complete(value).ToList();
                    if (matches.Count > 0)
                    {
                        dropdownMatches = matches;
                        OpenList(
                            "commands",
                            matches.Select(FormatCommand).ToList(),
                            index =>
                            {
                                input.Value = "/" + dropdownMatches[index].Name + " ";
                                FocusInput();
                            },
                            modal: false);
                        return;
                    }
                }

                if (overlay.IsOpen)
                {
                    overlay.Close();
                }
            }

            void Submit()
            {
                if (overlay.IsOpen)
                {
                    AcceptOverlay();
                    return;
                }

                var line = (input.Value ?? string.Empty).Trim();
                input.Value = string.Empty;
                lastInput = string.Empty;

                if (loginMode)
                {
                    loginMode = false;
                    input.Secret = false;
                    if (line.Length == 0)
                    {
                        model.PushNotice("(login cancelled)");
                    }
                    else
                    {
                        try
                        {
                            CredentialResolver.Keyring.Store(runtime.ProviderInfo.Name, line);
                            model.PushNotice($"stored an API key for provider '{runtime.ProviderInfo.Name}'");
                        }
                        catch (CredentialException error)
                        {
                            model.PushNotice($"login failed: {error.Message}");
                        }
                    }

                    FocusInput();
                    Refresh();
                    return;
                }

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
                    case SlashCommandKind.Login:
                        loginMode = true;
                        input.Secret = true;
                        model.PushNotice(
                            $"paste the API key for provider '{runtime.ProviderInfo.Name}' and press Enter (Esc cancels)");
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
                        model.PushNotice(string.Join("  ", runtime.ToolListing.Select(t => t.Name)));
                        break;
                    case SlashCommandKind.Skills:
                        model.PushNotice(runtime.Skills.Count == 0
                            ? "no skills discovered"
                            : string.Join("\n", runtime.Skills.Select(s => $"{s.Name} — {s.Description}")));
                        break;
                    case SlashCommandKind.Skill when command.Argument.Length > 0:
                        LoadSkill(command.Argument);
                        break;
                    case SlashCommandKind.Resume:
                        OpenResumeList();
                        break;
                    case SlashCommandKind.Model when command.Argument.Length > 0:
                        runtime.SetModel(command.Argument);
                        break;
                    case SlashCommandKind.Model:
                        if (model.Models.Count > 0)
                        {
                            OpenModelList();
                        }
                        else
                        {
                            modelPickerPending = true;
                            runtime.ListModels();
                            model.PushNotice("fetching models…");
                        }

                        break;
                    case SlashCommandKind.Provider when command.Argument.Length > 0:
                        runtime.SetProvider(command.Argument);
                        break;
                    case SlashCommandKind.Provider:
                        OpenList(
                            "provider",
                            Icarus.Core.Config.Providers.All.Select(p => FormatProvider(p.Name)).ToList(),
                            index => runtime.SetProvider(Icarus.Core.Config.Providers.All[index].Name),
                            modal: true);
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
                    case SlashCommandKind.Effort:
                        OpenList(
                            "effort",
                            EffortLevels,
                            index => runtime.SetEffort(EffortExtensions.Parse(EffortLevels[index])!.Value),
                            modal: true);
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

            void OpenModelList() =>
                OpenList(
                    "model",
                    model.Models.ToList(),
                    index => runtime.SetModel(model.Models[index]),
                    modal: true);

            void OpenResumeList()
            {
                var summaries = sessions.ListSessions(runtime.WorkspaceRoot);
                if (summaries.Count == 0)
                {
                    model.PushNotice("no previous session for this workspace");
                    return;
                }

                OpenList(
                    "resume",
                    summaries.Select(s => $"{s.Id}  {s.CreatedAt:yyyy-MM-dd HH:mm}").ToList(),
                    index =>
                    {
                        var history = sessions.LoadAt(summaries[index].Path);
                        runtime.ReplaceHistory(history);
                        model.LoadHistory(history);
                        model.PushNotice($"resumed session {summaries[index].Id}");
                    },
                    modal: true);
            }

            void LoadSkill(string name)
            {
                var skill = runtime.Skills.FirstOrDefault(s => s.Name == name);
                if (skill is null)
                {
                    model.PushNotice($"unknown skill '{name}' (see /skills)");
                    return;
                }

                string body;
                try
                {
                    body = File.ReadAllText(skill.Path);
                }
                catch (IOException error)
                {
                    model.PushNotice($"could not read skill '{name}': {error.Message}");
                    return;
                }

                var message = $"Follow these instructions:\n\n{body}";
                model.PushUser($"/skill {name}");
                if (model.Busy)
                {
                    model.PushNotice("(skill queued as steer)");
                    runtime.Steer(message);
                }
                else
                {
                    runtime.Prompt(message);
                }
            }

            Application.KeyDown += (_, key) =>
            {
                if (overlay.IsOpen)
                {
                    switch (key.KeyCode)
                    {
                        case KeyCode.CursorUp:
                            overlay.MoveUp();
                            break;
                        case KeyCode.CursorDown:
                            overlay.MoveDown();
                            break;
                        case KeyCode.Enter or KeyCode.Tab:
                            AcceptOverlay();
                            break;
                        case KeyCode.Esc:
                            CloseOverlay();
                            break;
                        default:
                            return; // let the input keep filtering the dropdown
                    }

                    key.Handled = true;
                    return;
                }

                if (loginMode)
                {
                    if (key.KeyCode == KeyCode.Esc)
                    {
                        loginMode = false;
                        input.Secret = false;
                        model.PushNotice("(login cancelled)");
                        key.Handled = true;
                        FocusInput();
                        Refresh();
                    }

                    return;
                }

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

            // Events are produced on the runtime worker thread and consumed on
            // the UI thread only: the pump just enqueues, the timer drains. This
            // avoids touching Terminal.Gui off-thread (which deadlocked when
            // events arrived before Application.Run started the UI loop).
            var pending = new ConcurrentQueue<Event>();
            _ = Task.Run(async () =>
            {
                await foreach (var @event in runtime.Events.ReadAllAsync())
                {
                    pending.Enqueue(@event);
                }
            });

            Application.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
            {
                spinnerFrame++;

                if (refocusPending)
                {
                    refocusPending = false;
                    FocusInput();
                }

                var changed = false;
                while (pending.TryDequeue(out var @event))
                {
                    model.Apply(@event);
                    changed = true;
                }

                if (modelPickerPending && model.Models.Count > 0)
                {
                    modelPickerPending = false;
                    OpenModelList();
                }

                var value = input.Value ?? string.Empty;
                if (value != lastInput)
                {
                    lastInput = value;
                    UpdateCompletions();
                }

                if (changed || model.Busy)
                {
                    Refresh();
                }

                return true;
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

    private static string FormatCommand(CommandSpec spec) =>
        spec.ArgumentHint is null ? "/" + spec.Name : $"/{spec.Name} {spec.ArgumentHint}";

    private static string FormatProvider(string name) =>
        name == "fake" ? "fake (offline)" : name;

    private static string HelpText() =>
        "commands: " + string.Join(", ", SlashCommands.All.Select(s => "/" + s.Name))
        + "  ·  keys: Enter send · Esc/Ctrl-C abort while busy, quit at prompt · PgUp/PgDn/↑↓ scroll";

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
