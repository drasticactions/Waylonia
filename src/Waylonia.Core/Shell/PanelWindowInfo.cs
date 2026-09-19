using Basin;

namespace Waylonia.Shell;

internal sealed record PanelWindowInfo(
    long Id,
    string Title,
    string AppId,
    string? Session,
    int Workspace,
    bool Sticky,
    bool Focused,
    bool Minimized,
    bool DemandsAttention,
    Box Frame,
    string? Icon = null);
