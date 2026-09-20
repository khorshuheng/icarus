using System.Diagnostics;
using Icarus.Core.Config;

namespace Icarus.Core.Credentials;

/// <summary>A best-effort secret store (ICARUS-102). Linux-only.</summary>
public interface ICredentialStore
{
    /// <summary>Store a key; throws <see cref="CredentialException"/> when the keyring refuses it.</summary>
    void Store(string provider, string key);

    string? Get(string provider);

    bool Delete(string provider);
}

/// <summary>A keyring failure that should be surfaced to the user.</summary>
public sealed class CredentialException(string message) : Exception(message);

/// <summary>A store that does nothing (no Secret Service available).</summary>
public sealed class NullCredentialStore : ICredentialStore
{
    public void Store(string provider, string key) => throw new CredentialException(
        "no OS keyring is available on this system (the libsecret `secret-tool` CLI was not found); "
        + "use --api-key or the provider environment variable instead");

    public string? Get(string provider) => null;

    public bool Delete(string provider) => false;
}

/// <summary>
/// Linux Secret Service access through the libsecret <c>secret-tool</c> CLI
/// (ICARUS-102). Used only when the binary is present; flag and environment
/// resolution always work regardless.
/// </summary>
public sealed class SecretToolCredentialStore : ICredentialStore
{
    private const string Service = "icarus";

    private SecretToolCredentialStore() { }

    /// <summary>The store to use by default: secret-tool when available, else a no-op.</summary>
    public static ICredentialStore CreateDefault() =>
        FindSecretTool() is not null ? new SecretToolCredentialStore() : new NullCredentialStore();

    public void Store(string provider, string key)
    {
        var (code, _, error) = Run(
            ["store", $"--label=icarus:{provider}", "service", Service, "provider", provider], key);
        if (code != 0)
        {
            throw new CredentialException(
                $"could not store the API key for '{provider}' in the keyring: "
                + (error.Length > 0 ? error : $"secret-tool exited with code {code}"));
        }
    }

    public string? Get(string provider)
    {
        var (code, output, _) = Run(["lookup", "service", Service, "provider", provider], stdin: null);
        return code == 0 && output.Length > 0 ? output : null;
    }

    public bool Delete(string provider)
    {
        var (code, _, _) = Run(["clear", "service", Service, "provider", provider], stdin: null);
        return code == 0;
    }

    private static string? FindSecretTool()
    {
        foreach (var candidate in new[] { "/usr/bin/secret-tool", "/usr/local/bin/secret-tool" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "secret-tool");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (int Code, string Output, string Error) Run(IReadOnlyList<string> arguments, string? stdin)
    {
        var tool = FindSecretTool();
        if (tool is null)
        {
            return (127, string.Empty, "secret-tool not found");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (127, string.Empty, "secret-tool could not be started");
        }

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.TrimEnd('\n', '\r'), error.TrimEnd('\n', '\r'));
    }
}

/// <summary>
/// API-key resolution: <c>--api-key</c> flag &gt; provider-native environment
/// variable &gt; OS keyring (ICARUS-102). Never reads the config file.
/// </summary>
public static class Credentials
{
    /// <summary>The keyring store; replaceable for tests.</summary>
    public static ICredentialStore Keyring { get; set; } = SecretToolCredentialStore.CreateDefault();

    /// <summary>Whether a real OS keyring is available on this system.</summary>
    public static bool KeyringAvailable => Keyring is not NullCredentialStore;

    public static string? Resolve(ProviderInfo provider, string? flag)
    {
        if (!string.IsNullOrEmpty(flag))
        {
            return flag;
        }

        if (provider.ApiKeyEnv is { } variable
            && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        return provider.RequiresKey ? Keyring.Get(provider.Name) : null;
    }
}
