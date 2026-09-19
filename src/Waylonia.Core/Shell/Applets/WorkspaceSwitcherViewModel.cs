using Basin.Shell.Nested;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Basin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Shell.Applets;

internal sealed partial class WorkspaceSwitcherViewModel : ObservableObject
{
    public const double DefaultCellWidth = 40;

    public const double DefaultCellHeight = 20;

    private readonly PanelModel _model;

    [ObservableProperty]
    private int _rows = 1;

    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    private double _cellWidth = DefaultCellWidth;

    [ObservableProperty]
    private double _cellHeight = DefaultCellHeight;

    public WorkspaceSwitcherViewModel(PanelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        Rebuild();
        model.PropertyChanged += OnModelChanged;
    }

    public ObservableCollection<WorkspaceCellViewModel> Cells { get; } = [];

    public static (int Rows, int Columns) Grid(int count, int rows)
    {
        var effectiveRows = Math.Clamp(rows, 1, Math.Max(1, count));
        return (effectiveRows, Math.Max(1, (count + effectiveRows - 1) / effectiveRows));
    }

    public static WorkspaceMiniatureViewModel Miniature(PanelWindowInfo window, Box workArea, double cellWidth, double cellHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        var scaleX = workArea.Width > 0 ? cellWidth / workArea.Width : 0;
        var scaleY = workArea.Height > 0 ? cellHeight / workArea.Height : 0;
        return new WorkspaceMiniatureViewModel(
            window.Id,
            (window.Frame.X - workArea.X) * scaleX,
            (window.Frame.Y - workArea.Y) * scaleY,
            Math.Max(1, window.Frame.Width * scaleX),
            Math.Max(1, window.Frame.Height * scaleY),
            window.Focused);
    }

    public void DropWindow(long id, int index) => _model.Commands.MoveToWorkspace(id, index);

    [RelayCommand]
    private void Switch(int index) => _model.Commands.SwitchWorkspace(index);

    partial void OnCellWidthChanged(double value) => Rebuild();

    partial void OnCellHeightChanged(double value) => Rebuild();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PanelModel.Windows) or nameof(PanelModel.Workspaces)
            or nameof(PanelModel.CurrentWorkspace) or nameof(PanelModel.WorkspaceRows) or nameof(PanelModel.WorkArea))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var workspaces = _model.Workspaces;
        (Rows, Columns) = Grid(workspaces.Count, _model.WorkspaceRows);
        while (Cells.Count > workspaces.Count)
        {
            Cells.RemoveAt(Cells.Count - 1);
        }

        for (var i = 0; i < workspaces.Count; i++)
        {
            if (i >= Cells.Count)
            {
                Cells.Add(new WorkspaceCellViewModel(_model.Commands, workspaces[i].Index));
            }
            else if (Cells[i].Index != workspaces[i].Index)
            {
                Cells[i] = new WorkspaceCellViewModel(_model.Commands, workspaces[i].Index);
            }

            Fill(Cells[i], workspaces[i]);
        }
    }

    private void Fill(WorkspaceCellViewModel cell, PanelWorkspaceInfo workspace)
    {
        cell.Name = workspace.Name;
        cell.IsCurrent = workspace.Index == _model.CurrentWorkspace;
        cell.Width = CellWidth;
        cell.Height = CellHeight;
        cell.Windows = _model.Windows
            .Where(window => !window.Minimized && (window.Sticky || window.Workspace == workspace.Index))
            .Select(window => Miniature(window, _model.WorkArea, CellWidth, CellHeight))
            .ToArray();
    }
}
