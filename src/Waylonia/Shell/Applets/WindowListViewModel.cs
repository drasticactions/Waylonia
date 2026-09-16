using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Waylonia.Shell.Applets;

internal sealed class WindowListViewModel : IExpandingApplet
{
    private readonly PanelModel _model;

    public WindowListViewModel(PanelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        Rebuild();
        model.PropertyChanged += OnModelChanged;
    }

    public ObservableCollection<WindowButtonViewModel> Buttons { get; } = [];

    public static bool Shows(PanelWindowInfo window, int currentWorkspace)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Sticky || window.Workspace == currentWorkspace;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PanelModel.Windows) or nameof(PanelModel.CurrentWorkspace))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var existing = Buttons.ToDictionary(button => button.Id);
        var wanted = new List<WindowButtonViewModel>();
        foreach (var window in _model.Windows.Where(window => Shows(window, _model.CurrentWorkspace)))
        {
            if (existing.TryGetValue(window.Id, out var button))
            {
                button.Update(window);
            }
            else
            {
                button = new WindowButtonViewModel(_model.Commands, window);
            }

            wanted.Add(button);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var index = Buttons.IndexOf(wanted[i]);
            if (index == i)
            {
                continue;
            }

            if (index < 0)
            {
                Buttons.Insert(i, wanted[i]);
            }
            else
            {
                Buttons.Move(index, i);
            }
        }

        while (Buttons.Count > wanted.Count)
        {
            Buttons.RemoveAt(Buttons.Count - 1);
        }
    }
}
