using System.Diagnostics;

namespace Icarus.Cli.Tests;

/// <summary>
/// Runs the built <c>icarus</c> binary as a child process (ICARUS-110). The
/// default tests exercise the CLI surface offline; the full-screen PTY smoke is
/// opt-in via <c>ICARUS_PTY_SMOKE=1</c> so the default suite cannot hang.
/// </summary>
public class BinarySmokeTests
{
    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var dll = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(dll);
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Version_flag_prints_the_version()
    {
        var (exitCode, stdout, _) = RunCli("--version");

        Assert.Equal(0, exitCode);
        Assert.Contains("icarus", stdout);
    }

    [Fact]
    public void Help_flag_prints_usage()
    {
        var (exitCode, stdout, _) = RunCli("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: icarus", stdout);
    }

    [Fact]
    public void A_non_terminal_run_is_refused_with_a_clear_message()
    {
        var (exitCode, _, stderr) = RunCli("--provider", "fake", "--model", "m", "--dir", "/tmp");

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a terminal", stderr);
    }
}
