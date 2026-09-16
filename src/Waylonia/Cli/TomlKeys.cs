using Tomlyn.Syntax;

namespace Waylonia.Cli;

internal static class TomlKeys
{
    public static string Text(KeySyntax? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        var parts = new List<string> { Part(key.Key) };
        foreach (var dotted in key.DotKeys)
        {
            parts.Add(Part(dotted.Key));
        }

        return string.Join('.', parts);
    }

    public static KeySyntax Make(string text) =>
        IsBare(text) ? new KeySyntax(text) : new KeySyntax { Key = new StringValueSyntax(text) };

    public static T At<T>(SyntaxList<T> list, int index)
        where T : SyntaxNode =>
        (T)list.GetChild(index)!;

    public static List<SyntaxTrivia> Movable(List<SyntaxTrivia> trivia, bool trimStart)
    {
        if (!trivia.Any(static item => item.Kind == TokenKind.Comment))
        {
            return [];
        }

        var start = 0;
        while (trimStart && start < trivia.Count && trivia[start].Kind is TokenKind.NewLine or TokenKind.Whitespaces)
        {
            start++;
        }

        return trivia.GetRange(start, trivia.Count - start);
    }

    public static bool IsBare(string text) =>
        text.Length > 0 && text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string Part(SyntaxNode? part) => part switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
        StringValueSyntax quoted => quoted.Value ?? string.Empty,
        _ => string.Empty,
    };
}
