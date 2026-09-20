namespace Icarus.Core.Tools;

/// <summary>UTF-16-safe truncation so a cap never splits a surrogate pair.</summary>
internal static class TextUtil
{
    public static string Tail(string text, int maxChars)
    {
        if (text.Length <= maxChars || maxChars <= 0)
        {
            return text;
        }

        var start = text.Length - maxChars;
        if (char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        return text[start..];
    }

    public static string Head(string text, int maxChars)
    {
        if (text.Length <= maxChars || maxChars <= 0)
        {
            return text;
        }

        var end = maxChars;
        if (char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }
}
