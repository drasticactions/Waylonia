using Waylonia.Sessions;

namespace Waylonia.Ui;

internal sealed record SessionMenuEntry(
    string Name,
    SessionStatus Status,
    IReadOnlyList<ApplicationMenuItem>? Applications,
    string? Notice,
    Action Connect,
    Action Disconnect,
    Action Refresh);
