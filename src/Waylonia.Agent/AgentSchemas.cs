using Basin.Ipc;

namespace Waylonia.Agent;

internal static class AgentSchemas
{
    public static IpcMethodInfo Describe { get; } = new(
        "Describe this agent session: the profile, headless or visible, the output size and scale, the Wayland socket and X display that launched applications use, whether launches wait for approval, which methods this compositor leaves out, whether accessibility is up and whether a person has paused the agent.",
        "{\"type\":\"object\",\"properties\":{}}",
        IpcMethodTraits.ReadOnly | IpcMethodTraits.Idempotent);

    public static IpcMethodInfo Launchable { get; } = new(
        "List the applications installed on this machine that waylonia/launch can start by desktop_id: each .desktop id with its name, comment and categories. Any other program on PATH starts by command.",
        "{\"type\":\"object\",\"properties\":{}}",
        IpcMethodTraits.ReadOnly | IpcMethodTraits.Idempotent);

    public static IpcMethodInfo Launch { get; } = new(
        "Start a program in this agent session: command plus args, or desktop_id. There is no shell. Any program starts at once unless the profile holds launches for a person's approval. The program gets the profile's own home, session bus and accessibility bus, and its output goes to a log that process/log reads. Returns the launch id and the pid. Wait for its window with windows/wait.",
        "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"The program, such as foot or /usr/bin/foot. Not with desktop_id.\"},\"args\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Arguments after the program.\"},\"desktop_id\":{\"type\":\"string\",\"description\":\"A .desktop id, such as org.gnome.TextEditor or org.gnome.TextEditor.desktop. Not with command.\"}},\"examples\":[{\"command\":\"foot\"}]}",
        IpcMethodTraits.None);

    public static IpcMethodInfo WindowInfo { get; } = new(
        "Describe what windows/list does not say about a window: the launch id of the waylonia/launch whose process tree owns it, whether it is an X11 window or a dialog, its parent, and whether its accessibility tree was found. Without id, it describes every window.",
        "{\"type\":\"object\",\"properties\":{\"id\":{\"anyOf\":[{\"type\":\"integer\",\"minimum\":0},{\"type\":\"string\",\"pattern\":\"^[0-9]+$\"}],\"description\":\"A window id from windows/list. A string of decimal digits is also accepted.\"}}}",
        IpcMethodTraits.ReadOnly | IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11yTree { get; } = new(
        "Read a window's accessibility tree as indented text, one node per line: [node] role \"name\" {states} @x,y wxh. Boxes are relative to the window's client area, the same coordinates that input/pointer-button takes with window. A last line in parentheses says when depth or max_nodes cut the tree short.",
        "{\"type\":\"object\",\"properties\":{\"window\":{\"anyOf\":[{\"type\":\"integer\",\"minimum\":0},{\"type\":\"string\",\"pattern\":\"^[0-9]+$\"}],\"description\":\"A window id from windows/list. A string of decimal digits is also accepted.\"},\"depth\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":64,\"description\":\"How deep to read. The default is 12.\"},\"max_nodes\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5000,\"description\":\"The most nodes to read. The default is 400.\"}},\"required\":[\"window\"]}",
        IpcMethodTraits.ReadOnly | IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11yFind { get; } = new(
        "Find nodes under a window by role, a case-insensitive name substring and states such as showing and enabled.",
        "{\"type\":\"object\",\"properties\":{\"window\":{\"anyOf\":[{\"type\":\"integer\",\"minimum\":0},{\"type\":\"string\",\"pattern\":\"^[0-9]+$\"}],\"description\":\"A window id from windows/list. A string of decimal digits is also accepted.\"},\"role\":{\"type\":\"string\",\"description\":\"An AT-SPI role name, such as push-button, text, menu-item or link.\"},\"name\":{\"type\":\"string\",\"description\":\"A case-insensitive substring of the node's name.\"},\"states\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"States every match has, such as showing, enabled, focusable.\"},\"max\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":500,\"description\":\"The most matches. The default is 50.\"}},\"required\":[\"window\"]}",
        IpcMethodTraits.ReadOnly | IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11yAction { get; } = new(
        "Invoke a node's action by name, such as click, activate, press or toggle. Without action, the node's first action. No coordinates are involved, so a covered or scrolled-away widget still works.",
        "{\"type\":\"object\",\"properties\":{\"node\":{\"type\":\"string\",\"description\":\"A node id from waylonia/a11y-tree or waylonia/a11y-find, busname:objectpath.\"},\"action\":{\"type\":\"string\",\"description\":\"The action name. The error lists the node's actions when the name is wrong.\"}},\"required\":[\"node\"]}",
        IpcMethodTraits.None);

    public static IpcMethodInfo A11ySetText { get; } = new(
        "Replace the whole text of an editable node, such as a text field or a document.",
        "{\"type\":\"object\",\"properties\":{\"node\":{\"type\":\"string\",\"description\":\"A node id from waylonia/a11y-tree or waylonia/a11y-find, busname:objectpath.\"},\"text\":{\"type\":\"string\"}},\"required\":[\"node\",\"text\"]}",
        IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11ySetValue { get; } = new(
        "Set the value of a node with a value, such as a slider or a spin button.",
        "{\"type\":\"object\",\"properties\":{\"node\":{\"type\":\"string\",\"description\":\"A node id from waylonia/a11y-tree or waylonia/a11y-find, busname:objectpath.\"},\"value\":{\"type\":\"number\"}},\"required\":[\"node\",\"value\"]}",
        IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11yFocus { get; } = new(
        "Give a node the keyboard focus inside its application.",
        "{\"type\":\"object\",\"properties\":{\"node\":{\"type\":\"string\",\"description\":\"A node id from waylonia/a11y-tree or waylonia/a11y-find, busname:objectpath.\"}},\"required\":[\"node\"]}",
        IpcMethodTraits.Idempotent);

    public static IpcMethodInfo A11yClick { get; } = new(
        "Click the center of a node through the compositor, for a widget with no action. The node's box must be inside the window and not covered by another window.",
        "{\"type\":\"object\",\"properties\":{\"window\":{\"anyOf\":[{\"type\":\"integer\",\"minimum\":0},{\"type\":\"string\",\"pattern\":\"^[0-9]+$\"}],\"description\":\"A window id from windows/list. A string of decimal digits is also accepted.\"},\"node\":{\"type\":\"string\",\"description\":\"A node id from waylonia/a11y-tree or waylonia/a11y-find, busname:objectpath.\"},\"button\":{\"type\":\"string\",\"enum\":[\"left\",\"right\",\"middle\"],\"description\":\"The default is left.\"}},\"required\":[\"window\",\"node\"]}",
        IpcMethodTraits.None);

    public static IpcMethodInfo A11yWaitText { get; } = new(
        "Wait until a node in a window has a name or text that contains the given text, and describe that node. It checks again on each accessibility event, not on a timer, and answers failed at the timeout.",
        "{\"type\":\"object\",\"properties\":{\"window\":{\"anyOf\":[{\"type\":\"integer\",\"minimum\":0},{\"type\":\"string\",\"pattern\":\"^[0-9]+$\"}],\"description\":\"A window id from windows/list. A string of decimal digits is also accepted.\"},\"text\":{\"type\":\"string\"},\"timeout_ms\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":120000,\"description\":\"How long to wait. The default is 5000.\"}},\"required\":[\"window\",\"text\"]}",
        IpcMethodTraits.ReadOnly);
}
