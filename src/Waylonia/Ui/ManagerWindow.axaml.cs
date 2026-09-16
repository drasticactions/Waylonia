using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Waylonia.Ui;

internal sealed partial class ManagerWindow : Window
{
    private bool _allowClose;

    public ManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
        this.FindControl<ListBox>("SessionList")!
            .AddHandler(ContextRequestedEvent, OnRowContextRequested, RoutingStrategies.Tunnel);
    }

    private static void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
        {
            e.Handled = true;
        }
    }

    public ManagerWindow(ManagerViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
    }

    public ManagerViewModel Model => (ManagerViewModel)DataContext!;

    public void AllowClose()
    {
        _allowClose = true;
        Model.Detach();
    }
}
