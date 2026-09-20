# ICARUS

A .NET port of [CRAB](https://github.com/khorshuheng/crab): a minimal, TUI-only
coding agent that inspects and edits files in a workspace by calling exactly
four built-in tools — `read`, `bash`, `edit`, `write`.

- **TUI only.** No server, no client/server split, no headless RPC modes.
- **Linux only.** XDG paths, Secret Service keyring, POSIX process groups.
- **Providers:** Amazon Bedrock (AWS default credential chain, i.e. compute
  credentials) and Anthropic — both via official vendor clients behind
  [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/)'s
  `IChatClient`, plus a scripted fake provider for offline tests.
- **Spec:** ICARUS-100 in the Librarian catalog, with children ICARUS-101..110.

## Layout

```
src/Icarus.Core/   the agent engine (terminal-free)
src/Icarus.Cli/    the TUI frontend (binary: icarus)
tests/             xUnit suites; the default suite is fully offline
```

## Build

```bash
dotnet build -warnaserror
dotnet test          # the default suite is fully offline
```

## Publish (Linux)

```bash
make publish                        # linux-x64 + linux-arm64, self-contained single file
# or
 dotnet publish src/Icarus.Cli -c Release -r linux-x64 --self-contained true \
   -p:PublishSingleFile=true -o artifacts/linux-x64
```

## Run

```bash
icarus --provider bedrock --model <bedrock-model-id> --region us-east-1
icarus --provider anthropic --model <claude-model-id>
```

Bedrock uses the standard AWS credential chain (compute credentials included);
Anthropic resolves its key from `--api-key`, `ANTHROPIC_API_KEY`, or the OS
keyring.

## PTY smoke (manual)

A bare pty is not enough to drive Terminal.Gui (it queries the terminal for
capabilities and needs a responding emulator), so the full-screen smoke is a
documented manual step rather than an automated test:

```bash
# in a real terminal
icarus --provider fake --model m --dir /tmp   # then type /exit
```

The automated suite covers the CLI surface directly (`--version`, `--help`, and
the non-TTY guard).
