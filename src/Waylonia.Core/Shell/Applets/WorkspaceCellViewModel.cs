using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Shell.Applets;

internal sealed partial class WorkspaceCellViewModel : ObservableObject
{
    private readonly IPanelCommands _commands;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private IReadOnlyList<WorkspaceMiniatureViewModel> _windows = [];

    public WorkspaceCellViewModel(IPanelCommands commands, int index)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        Index = index;
    }

    public int Index { get; }

    [RelayCommand]
    private void Switch() => _commands.SwitchWorkspace(Index);
}
