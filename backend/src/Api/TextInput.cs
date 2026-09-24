namespace LiaraDocsAssistant.Api;

public static class TextInput
{
    /// <summary>
    /// Truncates to at most maxChars UTF-16 chars, backing off one further
    /// char if the cut would otherwise land inside a surrogate pair (e.g. an
    /// emoji) and split it into an unpaired surrogate.
    /// </summary>
    public static string TruncateSafely(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }
        return text[..cut];
    }
}
