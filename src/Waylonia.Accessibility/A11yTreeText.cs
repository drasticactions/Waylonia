using System.Text;

namespace Waylonia.Accessibility;

/// <summary>
/// The text form of accessible nodes: one line per node, <c>[id] role "name" {state,state} @x,y wxh</c>.
/// </summary>
public static class A11yTreeText
{
    /// <summary>
    /// One node's line, indented two spaces per depth. The box is left out when it is unknown.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="depth">Its depth under the window root, which is depth 0.</param>
    /// <returns>The line, without a line break.</returns>
    public static string Line(A11yNodeInfo node, int depth)
    {
        var line = new StringBuilder();
        line.Append(' ', depth * 2);
        line.Append('[').Append(node.Id).Append("] ");
        line.Append(node.Role).Append(' ');
        line.Append(Quote(node.Name)).Append(' ');
        line.Append('{').AppendJoin(',', node.States).Append('}');
        if (node.Box is { } box)
        {
            line.Append(" @").Append(box.X).Append(',').Append(box.Y)
                .Append(' ').Append(box.Width).Append('x').Append(box.Height);
        }

        return line.ToString();
    }

    /// <summary>
    /// The closing line when the node cap stopped the walk.
    /// </summary>
    /// <param name="maxNodes">The cap.</param>
    /// <returns>The line.</returns>
    public static string NodeCapLine(int maxNodes) =>
        $"(stopped at the node cap of {maxNodes}; more nodes remain)";

    /// <summary>
    /// The closing line when the depth limit hid children.
    /// </summary>
    /// <param name="maxDepth">The limit.</param>
    /// <param name="hidden">How many nodes at the limit have children that were not listed.</param>
    /// <returns>The line.</returns>
    public static string DepthCapLine(int maxDepth, int hidden) =>
        $"(depth limit of {maxDepth} reached; the children of {hidden} {(hidden == 1 ? "node" : "nodes")} are not listed)";

    /// <summary>
    /// A name in double quotes, with quotes, backslashes and line breaks escaped so the line stays one line.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted text.</returns>
    public static string Quote(string text)
    {
        var quoted = new StringBuilder(text.Length + 2);
        quoted.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>
    /// Text cut to a length, with an ellipsis when something was cut.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The longest result, ellipsis included.</param>
    /// <returns>The text.</returns>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var keep = Math.Max(0, max - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return string.Concat(text.AsSpan(0, keep), "\u2026");
    }
}
