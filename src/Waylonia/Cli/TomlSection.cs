using Tomlyn.Syntax;

namespace Waylonia.Cli;

internal sealed class TomlSection
{
    private readonly SyntaxList<KeyValueSyntax> _items;
    private readonly Func<List<SyntaxTrivia>?> _inherit;
    private readonly Action<List<SyntaxTrivia>> _orphan;

    internal TomlSection(
        SyntaxList<KeyValueSyntax> items,
        Func<List<SyntaxTrivia>?> inherit,
        Action<List<SyntaxTrivia>> orphan)
    {
        _items = items;
        _inherit = inherit;
        _orphan = orphan;
    }

    public IEnumerable<string> Keys => _items.Select(static item => TomlKeys.Text(item.Key));

    public bool Has(string key) => Find(key) is not null;

    public string? Text(string key) => Find(key)?.Value switch
    {
        StringValueSyntax text => text.Value,
        ArraySyntax array => string.Join(
            ' ',
            array.Items.Select(static item => (item.Value as StringValueSyntax)?.Value?.Trim())
                .Where(static part => part is { Length: > 0 })),
        _ => null,
    };

    public void Set(string key, string value)
    {
        if (Find(key) is { } existing)
        {
            if (existing.Value is not StringValueSyntax text || text.Value != value)
            {
                Replace(existing, new StringValueSyntax(value));
            }

            return;
        }

        Add(key, new StringValueSyntax(value));
    }

    public void Set(string key, bool value)
    {
        if (Find(key) is { } existing)
        {
            if (existing.Value is not BooleanValueSyntax flag || flag.Value != value)
            {
                Replace(existing, new BooleanValueSyntax(value));
            }

            return;
        }

        Add(key, new BooleanValueSyntax(value));
    }

    public void Set(string key, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (Find(key) is { } existing)
        {
            if (existing.Value is not ArraySyntax array
                || !array.Items.Select(static item => (item.Value as StringValueSyntax)?.Value).SequenceEqual(values))
            {
                Replace(existing, new ArraySyntax(values.ToArray()));
            }

            return;
        }

        Add(key, new ArraySyntax(values.ToArray()));
    }

    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index < 0)
        {
            return false;
        }

        var item = TomlKeys.At(_items, index);
        var trivia = new List<SyntaxTrivia>();
        if (item.LeadingTrivia is { } leading)
        {
            trivia.AddRange(leading);
        }

        if (item.EndOfLineToken?.TrailingTrivia is { } trailing)
        {
            trivia.AddRange(trailing);
        }

        _items.RemoveChildAt(index);
        trivia = TomlKeys.Movable(trivia, trimStart: index < _items.ChildrenCount);
        if (trivia.Count == 0)
        {
            return true;
        }

        if (index < _items.ChildrenCount)
        {
            var next = TomlKeys.At(_items, index);
            next.LeadingTrivia ??= [];
            next.LeadingTrivia.InsertRange(0, trivia);
        }
        else if (index > 0)
        {
            var previous = TomlKeys.At(_items, index - 1);
            previous.EndOfLineToken ??= SyntaxFactory.NewLine();
            previous.EndOfLineToken.TrailingTrivia ??= [];
            previous.EndOfLineToken.TrailingTrivia.AddRange(trivia);
        }
        else
        {
            _orphan(trivia);
        }

        return true;
    }

    private KeyValueSyntax? Find(string key)
    {
        var index = IndexOf(key);
        return index < 0 ? null : TomlKeys.At(_items, index);
    }

    private int IndexOf(string key)
    {
        for (var i = 0; i < _items.ChildrenCount; i++)
        {
            if (TomlKeys.Text(TomlKeys.At(_items, i).Key) == key)
            {
                return i;
            }
        }

        return -1;
    }

    private void Add(string key, ValueSyntax value)
    {
        var item = new KeyValueSyntax(TomlKeys.Make(key), value) { EndOfLineToken = SyntaxFactory.NewLine() };
        if (_items.ChildrenCount > 0)
        {
            var last = TomlKeys.At(_items, _items.ChildrenCount - 1);
            last.EndOfLineToken ??= SyntaxFactory.NewLine();
            if (last.EndOfLineToken.TrailingTrivia is { Count: > 0 } tail)
            {
                item.EndOfLineToken.TrailingTrivia = [.. tail];
                last.EndOfLineToken.TrailingTrivia = null;
            }
        }
        else if (_inherit() is { Count: > 0 } inherited)
        {
            item.LeadingTrivia = [.. inherited, SyntaxFactory.NewLineTrivia()];
        }

        _items.Add(item);
    }

    private static void Replace(KeyValueSyntax item, ValueSyntax value)
    {
        var oldTail = LastToken(item.Value)?.TrailingTrivia;
        item.Value = value;
        if (oldTail is { Count: > 0 } && LastToken(value) is { } token)
        {
            token.TrailingTrivia = oldTail;
        }
    }

    private static SyntaxToken? LastToken(SyntaxNode? node) =>
        node?.Tokens(false).OfType<SyntaxToken>().LastOrDefault();
}
