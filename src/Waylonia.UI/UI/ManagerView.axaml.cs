using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Waylonia.UI;

internal sealed partial class ManagerView : UserControl
{
    public const int DefaultWidth = 760;

    public const int DefaultHeight = 640;

    public ManagerView()
    {
        AvaloniaXamlLoader.Load(this);
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
}
