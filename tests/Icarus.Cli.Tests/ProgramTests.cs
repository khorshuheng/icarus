using Icarus.Cli;

namespace Icarus.Cli.Tests;

public class ProgramTests
{
    [Fact]
    public void Version_is_reported()
    {
        Assert.False(string.IsNullOrWhiteSpace(Program.Version));
    }
}
