using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace Icarus.Core.Config;

/// <summary>
/// Loads and validates configuration (ICARUS-105). The config file is TOML
/// (Tomlyn); keys are read explicitly by their snake_case names so the schema
/// is unambiguous. Secrets are rejected: an <c>api_key</c> key fails the load.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Load configuration from an optional file plus flag overrides, then
    /// resolve defaults. <paramref name="apiKey"/> is the already-resolved key
    /// (ICARUS-102); it is never read from the file.
    /// </summary>
    public static Config Load(
        string defaultWorkspace,
        string? configPath = null,
        PartialConfig? flags = null,
        string? apiKey = null)
    {
        var file = configPath is null ? new PartialConfig() : LoadFile(configPath);
        return Resolve(defaultWorkspace, file, flags ?? new PartialConfig(), apiKey);
    }

    /// <summary>Read and validate a config file into a <see cref="PartialConfig"/>.</summary>
    public static PartialConfig LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigException($"config file '{path}' does not exist");
        }

        var text = File.ReadAllText(path);
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text)
                ?? throw new ConfigException($"config file '{path}' is empty");
        }
        catch (ConfigException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ConfigException($"config file '{path}' is not valid TOML: {e.Message}");
        }

        if (root.ContainsKey("api_key"))
        {
            throw new ConfigException(
                "`api_key` is not allowed in the config file; keys are resolved from "
                + "--api-key, the provider environment variable, or the OS keyring");
        }

        return new PartialConfig
        {
            Provider = Str(root, "provider"),
            Model = Str(root, "model"),
            Region = Str(root, "region"),
            BaseUrl = Str(root, "base_url"),
            Temperature = Float(root, "temperature"),
            MaxIterations = Int(root, "max_iterations"),
            MaxOutputBytes = Int(root, "max_output_bytes"),
            MaxTokens = Int(root, "max_tokens"),
            TimeoutSecs = Int(root, "timeout_secs"),
            BashTimeoutSecs = Int(root, "bash_timeout_secs"),
            MaxRetries = Int(root, "max_retries"),
            MaxContextTokens = Int(root, "max_context_tokens"),
            SessionRetention = Int(root, "session_retention"),
            Workspace = Str(root, "workspace"),
            ThemeName = PartialConfig.ThemeNameFrom(root),
        };
    }

    /// <summary>
    /// Merge <paramref name="file"/> then <paramref name="flags"/> over
    /// defaults, validating the provider, model and numeric ranges.
    /// </summary>
    public static Config Resolve(
        string defaultWorkspace,
        PartialConfig file,
        PartialConfig flags,
        string? apiKey)
    {
        var merged = file.Overlay(flags);

        var providerName = merged.Provider ?? Providers.Default.Name;
        var provider = Providers.ByName(providerName)
            ?? throw new ConfigException(
                $"unknown provider '{providerName}' (supported: {Providers.SupportedNames})");

        var model = merged.Model?.Trim() ?? string.Empty;
        if (model.Length == 0)
        {
            throw new ConfigException(
                $"no model configured for provider '{provider.Name}' "
                + "(set `model` in config.toml or pass --model)");
        }

        var workspace = merged.Workspace is { Length: > 0 } ws ? ws : defaultWorkspace;

        var config = new Config
        {
            Provider = provider,
            Model = model,
            Workspace = workspace,
            ApiKey = apiKey,
            Region = merged.Region,
            BaseUrl = merged.BaseUrl,
            Temperature = merged.Temperature ?? 0.7f,
            MaxIterations = merged.MaxIterations ?? 30,
            MaxOutputBytes = merged.MaxOutputBytes ?? 32_000,
            MaxTokens = merged.MaxTokens ?? 2_048,
            TimeoutSecs = merged.TimeoutSecs ?? 60,
            BashTimeoutSecs = merged.BashTimeoutSecs ?? 120,
            MaxRetries = merged.MaxRetries ?? 2,
            MaxContextTokens = merged.MaxContextTokens ?? (32_000 - 4_096),
            SessionRetention = merged.SessionRetention ?? 10,
            ThemeName = merged.ThemeName ?? "dark",
        };

        Validate(config);
        return config;
    }

    private static void Validate(Config config)
    {
        Positive(config.MaxIterations, "max_iterations");
        Positive(config.MaxOutputBytes, "max_output_bytes");
        Positive(config.MaxTokens, "max_tokens");
        Positive(config.TimeoutSecs, "timeout_secs");
        Positive(config.MaxContextTokens, "max_context_tokens");
        NonNegative(config.BashTimeoutSecs, "bash_timeout_secs");
        NonNegative(config.MaxRetries, "max_retries");
        NonNegative(config.SessionRetention, "session_retention");

        if (config.Temperature is < 0f or > 2f)
        {
            throw new ConfigException($"temperature {config.Temperature} is out of range (0.0–2.0)");
        }
    }

    private static void Positive(int value, string key)
    {
        if (value <= 0)
        {
            throw new ConfigException($"`{key}` must be greater than 0 (got {value})");
        }
    }

    private static void NonNegative(int value, string key)
    {
        if (value < 0)
        {
            throw new ConfigException($"`{key}` must not be negative (got {value})");
        }
    }

    private static string? Str(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static int? Int(TomlTable table, string key) =>
        table.TryGetValue(key, out var value)
            ? value switch
            {
                long n => checked((int)n),
                int n => n,
                _ => null,
            }
            : null;

    private static float? Float(TomlTable table, string key) =>
        table.TryGetValue(key, out var value)
            ? value switch
            {
                double d => (float)d,
                float f => f,
                long n => n,
                _ => null,
            }
            : null;
}
