namespace Waylonia;

/// <summary>
/// Turns a fragment into a UI sentence: the first letter capitalized and one final period.
/// </summary>
internal static class UiSentence
{
    /// <summary>
    /// Capitalizes the first letter of the text and ends it with a period, unless it already ends a sentence.
    /// </summary>
    /// <param name="text">The text, which can be a lowercase fragment or a sentence.</param>
    /// <returns>The sentence, or an empty string for empty text.</returns>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var capitalized = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return capitalized[^1] is '.' or '!' or '?' ? capitalized : capitalized + ".";
    }
}
