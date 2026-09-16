using Tomlyn;
using Tomlyn.Syntax;

namespace Waylonia.Cli;

internal sealed class TomlDocument
{
    private readonly DocumentSyntax _document;

    private TomlDocument(DocumentSyntax document) => _document = document;

    public static TomlDocument Empty() => new(new DocumentSyntax());

    public static TomlDocument? Parse(string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = Toml.Parse(text);
        if (document.HasErrors)
        {
            error = string.Join(
                "; ",
                document.Diagnostics
                    .Where(static message => message.Kind == DiagnosticMessageKind.Error)
                    .Select(static message => message.ToString()));
            return null;
        }

        error = null;
        return new TomlDocument(document);
    }

    public TomlSection Root => new(_document.KeyValues, TakeDocumentTrivia, GiveDocumentTrivia);

    public TomlSection? Table(params string[] path) =>
        FindTable(string.Join('.', path)) is { } table ? Section(table) : null;

    public TomlSection EnsureTable(params string[] path)
    {
        var name = string.Join('.', path);
        if (FindTable(name) is { } existing)
        {
            return Section(existing);
        }

        var table = new TableSyntax(path.Length == 2 ? new KeySyntax(path[0], path[1]) : new KeySyntax(name))
        {
            EndOfLineToken = SyntaxFactory.NewLine(),
        };
        if (TakeDocumentTrivia() is { Count: > 0 } inherited)
        {
            table.LeadingTrivia = [.. inherited, SyntaxFactory.NewLineTrivia()];
        }
        else
        {
            table.LeadingTrivia = [SyntaxFactory.NewLineTrivia()];
            if (_document.Tables.ChildrenCount == 0 && _document.KeyValues.ChildrenCount > 0)
            {
                TomlKeys.At(_document.KeyValues, _document.KeyValues.ChildrenCount - 1).EndOfLineToken ??= SyntaxFactory.NewLine();
            }
            else if (_document.Tables.ChildrenCount > 0
                && TomlKeys.At(_document.Tables, _document.Tables.ChildrenCount - 1) is { } last)
            {
                EndLine(last);
            }
        }

        _document.Tables.Add(table);
        return Section(table);
    }

    public bool RemoveTable(params string[] path)
    {
        var name = string.Join('.', path);
        for (var i = 0; i < _document.Tables.ChildrenCount; i++)
        {
            var table = TomlKeys.At(_document.Tables, i);
            if (table is not TableSyntax || TomlKeys.Text(table.Name) != name)
            {
                continue;
            }

            var trivia = new List<SyntaxTrivia>();
            if (table.LeadingTrivia is { } leading)
            {
                trivia.AddRange(leading);
            }

            if (TailToken(table)?.TrailingTrivia is { } tail)
            {
                trivia.AddRange(tail);
            }

            _document.Tables.RemoveChildAt(i);
            trivia = TomlKeys.Movable(trivia, trimStart: i < _document.Tables.ChildrenCount);
            if (trivia.Count == 0)
            {
                return true;
            }

            if (i < _document.Tables.ChildrenCount)
            {
                var next = TomlKeys.At(_document.Tables, i);
                next.LeadingTrivia ??= [];
                next.LeadingTrivia.InsertRange(0, trivia);
            }
            else if (_document.Tables.ChildrenCount > 0)
            {
                var last = TomlKeys.At(_document.Tables, _document.Tables.ChildrenCount - 1);
                EndLine(last);
                var token = TailToken(last)!;
                token.TrailingTrivia ??= [];
                token.TrailingTrivia.AddRange(trivia);
            }
            else
            {
                GiveDocumentTrivia(trivia);
            }

            return true;
        }

        return false;
    }

    public IReadOnlyList<string> Tables(string prefix)
    {
        var names = new List<string>();
        foreach (var table in _document.Tables)
        {
            if (table is TableSyntax
                && table.Name is { } key
                && TomlKeys.Text(key) is { } name
                && name.StartsWith(prefix + ".", StringComparison.Ordinal)
                && key.DotKeys.ChildrenCount == 1)
            {
                names.Add(name[(prefix.Length + 1)..]);
            }
        }

        return names;
    }

    public string Render()
    {
        using var writer = new StringWriter();
        _document.WriteTo(writer);
        return writer.ToString();
    }

    private TableSyntaxBase? FindTable(string name)
    {
        foreach (var table in _document.Tables)
        {
            if (table is TableSyntax && TomlKeys.Text(table.Name) == name)
            {
                return table;
            }
        }

        return null;
    }

    private static TomlSection Section(TableSyntaxBase table) => new(
        table.Items,
        static () => null,
        trivia =>
        {
            table.EndOfLineToken ??= SyntaxFactory.NewLine();
            table.EndOfLineToken.TrailingTrivia ??= [];
            table.EndOfLineToken.TrailingTrivia.AddRange(trivia);
        });

    private static SyntaxToken? TailToken(TableSyntaxBase table) =>
        table.Items.ChildrenCount > 0
            ? TomlKeys.At(table.Items, table.Items.ChildrenCount - 1).EndOfLineToken
            : table.EndOfLineToken;

    private static void EndLine(TableSyntaxBase table)
    {
        if (table.Items.ChildrenCount > 0)
        {
            TomlKeys.At(table.Items, table.Items.ChildrenCount - 1).EndOfLineToken ??= SyntaxFactory.NewLine();
        }
        else
        {
            table.EndOfLineToken ??= SyntaxFactory.NewLine();
        }
    }

    private List<SyntaxTrivia>? TakeDocumentTrivia()
    {
        if (_document.KeyValues.ChildrenCount > 0 || _document.Tables.ChildrenCount > 0)
        {
            return null;
        }

        var trivia = _document.TrailingTrivia;
        _document.TrailingTrivia = null;
        return trivia;
    }

    private void GiveDocumentTrivia(List<SyntaxTrivia> trivia)
    {
        if (_document.Tables.ChildrenCount > 0)
        {
            var first = TomlKeys.At(_document.Tables, 0);
            first.LeadingTrivia ??= [];
            first.LeadingTrivia.InsertRange(0, trivia);
            return;
        }

        if (_document.KeyValues.ChildrenCount > 0)
        {
            var last = TomlKeys.At(_document.KeyValues, _document.KeyValues.ChildrenCount - 1);
            last.EndOfLineToken ??= SyntaxFactory.NewLine();
            last.EndOfLineToken.TrailingTrivia ??= [];
            last.EndOfLineToken.TrailingTrivia.AddRange(trivia);
            return;
        }

        _document.TrailingTrivia ??= [];
        _document.TrailingTrivia.InsertRange(0, trivia);
    }
}
