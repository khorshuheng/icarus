using System.Diagnostics;
using Icarus.Core.Config;

namespace Icarus.Core.Credentials;

/// <summary>A best-effort secret store (ICARUS-102). Linux-only.</summary>
public interface ICredentialStore
{
    void Store(string provider, string key);

    string? Get(string provider);

    bool Delete(string provider);
}

/// <summary>A store that does nothing (no Secret Service available).</summary>
public sealed class NullCredentialStore : ICredentialStore
{
    public void Store(string provider, string key) { }

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

    public void Store(string provider, string key) =>
        Run(["store", $"--label=icarus:{provider}", "service", Service, "provider", provider], key);

    public string? Get(string provider)
    {
        var output = Run(["lookup", "service", Service, "provider", provider], stdin: null);
        return string.IsNullOrEmpty(output) ? null : output;
    }

    public bool Delete(string provider)
    {
        Run(["clear", "service", Service, "provider", provider], stdin: null);
        return true;
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

    private static string Run(IReadOnlyList<string> arguments, string? stdin)
    {
        var tool = FindSecretTool();
        if (tool is null)
        {
            return string.Empty;
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
            return string.Empty;
        }

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.TrimEnd('\n', '\r');
    }
}

/// <summary>
/// API-key resolution: <c>--api-key</c> flag &gt; provider-native environment
/// variable &gt; OS keyring (ICARUS-102). Never reads the config file.
/// </summary>
public static class Credentials
{
    /// <summary>The keyring store; replaceable for tests.</summary>
    public static ICredentialStore Store { get; set; } = SecretToolCredentialStore.CreateDefault();

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

        return provider.RequiresKey ? Store.Get(provider.Name) : null;
    }
}
