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

    public const string ImportKeyLabel = "Import key…";

    private MenuItem? _importKey;

    public ManagerView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ListBox>("SessionList")!
            .AddHandler(ContextRequestedEvent, OnRowContextRequested, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => OfferKeyImport();
        OfferKeyImport();
    }

    private void OfferKeyImport()
    {
        if (this.FindControl<Menu>("MenuBar")?.Items.OfType<MenuItem>().FirstOrDefault() is not { } session)
        {
            return;
        }

        if (_importKey is { } stale)
        {
            session.Items.Remove(stale);
            _importKey = null;
        }

        if (DataContext is ManagerViewModel { CanImportKey: true } model)
        {
            _importKey = new MenuItem { Header = ImportKeyLabel, Command = model.ImportKeyCommand };
            session.Items.Add(_importKey);
        }
    }

    private static void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
        {
            e.Handled = true;
        }
    }
}
