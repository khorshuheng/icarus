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
dotnet test
```
