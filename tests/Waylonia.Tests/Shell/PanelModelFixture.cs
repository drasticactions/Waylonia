using Basin;
using Waylonia.Sessions;
using Waylonia.Shell;

namespace Waylonia.Tests.Shell;

internal static class PanelModelFixture
{
    public static readonly Box WorkArea = new(0, 24, 800, 552);

    public static PanelModel Model(FakePanelCommands? commands = null, int workspaces = 4, int rows = 1)
    {
        var model = new PanelModel(commands ?? new FakePanelCommands())
        {
            WorkspaceRows = rows,
            WorkArea = WorkArea,
        };
        model.Workspaces = Enumerable.Range(0, workspaces)
            .Select(index => new PanelWorkspaceInfo(index, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        return model;
    }

    public static PanelWindowInfo Window(
        long id,
        string title = "",
        string appId = "app",
        int workspace = 0,
        bool sticky = false,
        bool focused = false,
        bool minimized = false,
        bool attention = false,
        string? session = null,
        Box? frame = null) =>
        new(id, title, appId, session, workspace, sticky, focused, minimized, attention, frame ?? new Box(0, 24, 400, 276));

    public static PanelSessionInfo Session(string name, SessionStatus status = SessionStatus.Connected) => new(name, status);
}
