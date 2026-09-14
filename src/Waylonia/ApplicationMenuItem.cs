namespace Waylonia;

internal sealed record ApplicationMenuItem(
    string Label,
    string? Command = null,
    IReadOnlyList<ApplicationMenuItem>? Children = null,
    bool Separator = false,
    string? Session = null,
    Action? Invoke = null)
{
    public ApplicationMenuItem InSession(string session) => this with
    {
        Session = Command is null ? Session : session,
        Children = Children?.Select(child => child.InSession(session)).ToArray(),
    };
}
