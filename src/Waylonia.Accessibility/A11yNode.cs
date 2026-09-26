namespace Waylonia.Accessibility;

internal sealed record A11yNode(
    A11yNodeRef Ref,
    uint Role,
    string Name,
    uint[] States,
    string[] Interfaces,
    int ChildCount)
{
    public const string AccessibleInterface = "org.a11y.atspi.Accessible";
    public const string ActionInterface = "org.a11y.atspi.Action";
    public const string ComponentInterface = "org.a11y.atspi.Component";
    public const string TextInterface = "org.a11y.atspi.Text";
    public const string EditableTextInterface = "org.a11y.atspi.EditableText";
    public const string ValueInterface = "org.a11y.atspi.Value";

    public bool Implements(string @interface) => Array.IndexOf(Interfaces, @interface) >= 0;

    public string RoleName => A11yNames.Role(Role);

    public IReadOnlyList<string> StateNames => A11yNames.States(States);
}
