using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BluerCurve.Chrome;

namespace Waylonia.UI;

internal sealed partial class ManagerWindow : BluerCurveWindow
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
    }

    public ManagerWindow(ManagerViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
    }

    public ManagerViewModel Model => (ManagerViewModel)DataContext!;

    public ManagerView View => (ManagerView)Content!;

    public void AllowClose()
    {
        _allowClose = true;
        Model.Detach();
    }
}
