using Wcwidth;

namespace Icarus.Cli.Ui;

/// <summary>Terminal display width (CJK/zero-width aware), via the Wcwidth package.</summary>
internal static class DisplayWidth
{
    public static int Of(string text) => string.IsNullOrEmpty(text) ? 0 : UnicodeCalculator.GetWidth(text);

    public static int Of(char value) => UnicodeCalculator.GetWidth(value);
}
