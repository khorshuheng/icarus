using Icarus.Core.Config;

namespace Icarus.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void Flags_beat_file_beat_defaults()
    {
        var file = new PartialConfig { Model = "from-file", Temperature = 0.1f, MaxIterations = 5 };
        var flags = new PartialConfig { Model = "from-flags" };

        var config = ConfigLoader.Resolve("/tmp/ws", file, flags, apiKey: null);

        Assert.Equal("from-flags", config.Model);
        Assert.Equal(0.1f, config.Temperature); // file wins over default
        Assert.Equal(5, config.MaxIterations);
    }

    [Fact]
    public void Applies_crab_defaults()
    {
        var config = ConfigLoader.Resolve("/tmp/ws", new PartialConfig { Model = "m" }, new PartialConfig(), null);

        Assert.Equal(Providers.Default, config.Provider);
        Assert.Equal("/tmp/ws", config.Workspace);
        Assert.Equal(0.7f, config.Temperature);
        Assert.Equal(30, config.MaxIterations);
        Assert.Equal(32_000, config.MaxOutputBytes);
        Assert.Equal(2_048, config.MaxTokens);
        Assert.Equal(60, config.TimeoutSecs);
        Assert.Equal(120, config.BashTimeoutSecs);
        Assert.Equal(2, config.MaxRetries);
        Assert.Equal(32_000 - 4_096, config.MaxContextTokens);
        Assert.Equal(10, config.SessionRetention);
        Assert.Equal("dark", config.ThemeName);
    }

    [Fact]
    public void Rejects_api_key_in_the_config_file()
    {
        using var file = TempFile.Write("""
            model = "claude"

            [theme]
            name = "dark"
            """);

        var parsed = ConfigLoader.LoadFile(file.Path);
        Assert.Equal("claude", parsed.Model);
        Assert.Equal("dark", parsed.ThemeName);

        using var secret = TempFile.Write("""
            model = "claude"
            api_key = "sk-secret"
            """);

        var error = Assert.Throws<ConfigException>(() => ConfigLoader.LoadFile(secret.Path));
        Assert.Contains("api_key", error.Message);
    }

    [Fact]
    public void Unknown_provider_fails_with_the_supported_list()
    {
        var error = Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws", new PartialConfig { Provider = "openai", Model = "m" }, new PartialConfig(), null));

        Assert.Contains("unknown provider 'openai'", error.Message);
        Assert.Contains("bedrock", error.Message);
        Assert.Contains("anthropic", error.Message);
        Assert.Contains("fake", error.Message);
    }

    [Fact]
    public void Missing_model_fails_with_guidance()
    {
        var error = Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws", new PartialConfig(), new PartialConfig(), null));

        Assert.Contains("no model configured", error.Message);
    }

    [Fact]
    public void Invalid_numbers_and_temperature_fail()
    {
        Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws", new PartialConfig { Model = "m", MaxIterations = 0 }, new PartialConfig(), null));
        Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws", new PartialConfig { Model = "m", Temperature = 5f }, new PartialConfig(), null));
        Assert.Throws<ConfigException>(() => ConfigLoader.Resolve(
            "/tmp/ws", new PartialConfig { Model = "m", MaxRetries = -1 }, new PartialConfig(), null));
    }

    [Fact]
    public void Parses_region_and_base_url()
    {
        using var file = TempFile.Write("""
            provider = "bedrock"
            model = "anthropic.claude-3-5-sonnet-20241022-v2:0"
            region = "us-west-2"
            base_url = "https://example.invalid"
            """);

        var config = ConfigLoader.Resolve("/tmp/ws", ConfigLoader.LoadFile(file.Path), new PartialConfig(), null);

        Assert.Equal("bedrock", config.Provider.Name);
        Assert.Equal("us-west-2", config.Region);
        Assert.Equal("https://example.invalid", config.EffectiveBaseUrl);
    }

    [Fact]
    public void An_explicit_missing_config_file_fails()
    {
        Assert.Throws<ConfigException>(() => ConfigLoader.Load("/tmp/ws", "/nonexistent/icarus.toml"));
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Write(string contents)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "icarus-config-" + Guid.NewGuid().ToString("N") + ".toml");
            File.WriteAllText(path, contents);
            return new TempFile(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
