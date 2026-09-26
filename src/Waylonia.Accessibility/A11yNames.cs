namespace Waylonia.Accessibility;

/// <summary>
/// The lowercase, dash-separated names AT-SPI gives its roles and states.
/// </summary>
public static class A11yNames
{
    private static readonly string[] Roles =
    [
        "invalid", "accelerator-label", "alert", "animation", "arrow", "calendar", "canvas",
        "check-box", "check-menu-item", "color-chooser", "column-header", "combo-box",
        "date-editor", "desktop-icon", "desktop-frame", "dial", "dialog", "directory-pane",
        "drawing-area", "file-chooser", "filler", "focus-traversable", "font-chooser", "frame",
        "glass-pane", "html-container", "icon", "image", "internal-frame", "label", "layered-pane",
        "list", "list-item", "menu", "menu-bar", "menu-item", "option-pane", "page-tab",
        "page-tab-list", "panel", "password-text", "popup-menu", "progress-bar", "push-button",
        "radio-button", "radio-menu-item", "root-pane", "row-header", "scroll-bar", "scroll-pane",
        "separator", "slider", "spin-button", "split-pane", "status-bar", "table", "table-cell",
        "table-column-header", "table-row-header", "tearoff-menu-item", "terminal", "text",
        "toggle-button", "tool-bar", "tool-tip", "tree", "tree-table", "unknown", "viewport",
        "window", "extended", "header", "footer", "paragraph", "ruler", "application",
        "autocomplete", "editbar", "embedded", "entry", "chart", "caption", "document-frame",
        "heading", "page", "section", "redundant-object", "form", "link", "input-method-window",
        "table-row", "tree-item", "document-spreadsheet", "document-presentation", "document-text",
        "document-web", "document-email", "comment", "list-box", "grouping", "image-map",
        "notification", "info-bar", "level-bar", "title-bar", "block-quote", "audio", "video",
        "definition", "article", "landmark", "log", "marquee", "math", "rating", "timer", "static",
        "math-fraction", "math-root", "subscript", "superscript", "description-list",
        "description-term", "description-value", "footnote", "content-deletion",
        "content-insertion", "mark", "suggestion", "push-button-menu", "switch"
    ];

    private static readonly string[] StateNames =
    [
        "invalid", "active", "armed", "busy", "checked", "collapsed", "defunct", "editable",
        "enabled", "expandable", "expanded", "focusable", "focused", "has-tooltip", "horizontal",
        "iconified", "modal", "multi-line", "multiselectable", "opaque", "pressed", "resizable",
        "selectable", "selected", "sensitive", "showing", "single-line", "stale", "transient",
        "vertical", "visible", "manages-descendants", "indeterminate", "required", "truncated",
        "animated", "invalid-entry", "supports-autocompletion", "selectable-text", "is-default",
        "visited", "checkable", "has-popup", "read-only"
    ];

    /// <summary>
    /// The name of a role, such as <c>push-button</c>, or <c>role-N</c> for a number this table does not know.
    /// </summary>
    /// <param name="role">The AT-SPI role number.</param>
    /// <returns>The name.</returns>
    public static string Role(uint role) => role < Roles.Length ? Roles[role] : $"role-{role}";

    /// <summary>
    /// The name of a state, such as <c>showing</c>, or <c>state-N</c> for a number this table does not know.
    /// </summary>
    /// <param name="state">The AT-SPI state number.</param>
    /// <returns>The name.</returns>
    public static string State(int state) => state >= 0 && state < StateNames.Length ? StateNames[state] : $"state-{state}";

    /// <summary>
    /// The names of the states set in an AT-SPI state set, bit N of word N / 32 standing for state N.
    /// </summary>
    /// <param name="bits">The words <c>GetState</c> returns.</param>
    /// <returns>The names in state order.</returns>
    public static IReadOnlyList<string> States(IReadOnlyList<uint> bits)
    {
        var names = new List<string>();
        for (var word = 0; word < bits.Count; word++)
        {
            for (var bit = 0; bit < 32; bit++)
            {
                if ((bits[word] & (1u << bit)) != 0)
                {
                    names.Add(State(word * 32 + bit));
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Whether a state set holds one state.
    /// </summary>
    /// <param name="bits">The words <c>GetState</c> returns.</param>
    /// <param name="state">The state.</param>
    /// <returns>True when the bit is set.</returns>
    public static bool Has(IReadOnlyList<uint> bits, AtSpiState state)
    {
        var index = (int)state;
        return index / 32 < bits.Count && (bits[index / 32] & (1u << (index % 32))) != 0;
    }

    /// <summary>
    /// Whether a role name given by a caller means this role: case is ignored, and spaces or underscores stand for dashes.
    /// </summary>
    /// <param name="wanted">The name the caller gave.</param>
    /// <param name="role">The AT-SPI role number.</param>
    /// <returns>True when they match.</returns>
    public static bool RoleMatches(string wanted, uint role) =>
        string.Equals(Normalize(wanted), Role(role), StringComparison.Ordinal);

    /// <summary>
    /// A role or state name folded to lowercase with dashes.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The folded name.</returns>
    public static string Normalize(string name) =>
        name.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');

    /// <summary>
    /// Whether a role is one a top-level window node carries: frame, window, dialog or alert.
    /// </summary>
    /// <param name="role">The AT-SPI role number.</param>
    /// <returns>True for the top-level roles.</returns>
    public static bool IsWindowRole(uint role) =>
        role is (uint)AtSpiRole.Frame or (uint)AtSpiRole.Window or (uint)AtSpiRole.Dialog or (uint)AtSpiRole.Alert;
}
