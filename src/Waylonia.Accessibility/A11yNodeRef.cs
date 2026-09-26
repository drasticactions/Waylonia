using System.Diagnostics.CodeAnalysis;

namespace Waylonia.Accessibility;

/// <summary>
/// One accessible object: the bus name that owns it and its object path.
/// </summary>
/// <param name="Bus">The bus name, usually a unique name such as <c>:1.42</c>.</param>
/// <param name="Path">The object path.</param>
public readonly record struct A11yNodeRef(string Bus, string Path)
{
    /// <summary>
    /// The object path AT-SPI uses for "no object".
    /// </summary>
    public const string NullPath = "/org/a11y/atspi/null";

    /// <summary>
    /// The node id, <c>busname:objectpath</c>.
    /// </summary>
    public string Id => $"{Bus}:{Path}";

    /// <summary>
    /// Whether this is AT-SPI's null reference.
    /// </summary>
    public bool IsNull => Bus.Length == 0 || Path == NullPath;

    /// <inheritdoc />
    public override string ToString() => Id;

    /// <summary>
    /// Reads a node id back.
    /// </summary>
    /// <param name="id">The id, <c>busname:objectpath</c>.</param>
    /// <param name="node">The reference.</param>
    /// <returns>False when the text is not a node id.</returns>
    public static bool TryParse(string? id, [NotNullWhen(true)] out A11yNodeRef node)
    {
        node = default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var split = id.IndexOf(":/", StringComparison.Ordinal);
        if (split <= 0)
        {
            return false;
        }

        var bus = id[..split];
        var path = id[(split + 1)..];
        if (bus.Contains('/') || path.Contains("//", StringComparison.Ordinal) ||
            (path.Length > 1 && path.EndsWith('/')))
        {
            return false;
        }

        foreach (var c in path)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '/'))
            {
                return false;
            }
        }

        node = new A11yNodeRef(bus, path);
        return true;
    }

    /// <summary>
    /// Reads a node id back, or throws the sentence that says what an id looks like.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The reference.</returns>
    public static A11yNodeRef Parse(string? id) =>
        TryParse(id, out var node)
            ? node
            : throw new A11yException(
                $"\"{id}\" is not a node id; ids look like :1.42:/org/a11y/atspi/accessible/7, as the tree prints them");
}
