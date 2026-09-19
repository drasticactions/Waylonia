using Basin.Shell.Nested;
using Basin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Waylonia.Shell;

internal sealed partial class PanelModel : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<PanelWindowInfo> _windows = [];

    [ObservableProperty]
    private IReadOnlyList<PanelWorkspaceInfo> _workspaces = [];

    [ObservableProperty]
    private int _currentWorkspace;

    [ObservableProperty]
    private int _workspaceRows = 1;

    [ObservableProperty]
    private Box _workArea;

    [ObservableProperty]
    private IReadOnlyList<PanelSessionInfo> _sessions = [];

    [ObservableProperty]
    private IReadOnlyList<ApplicationMenuItem> _applications = [];

    [ObservableProperty]
    private bool _settingsAvailable;

    [ObservableProperty]
    private PanelIcons _icons = PanelIcons.None;

    [ObservableProperty]
    private bool _softKeyboardOpen;

    public PanelModel(IPanelCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Commands = commands;
    }

    public IPanelCommands Commands { get; }
}
