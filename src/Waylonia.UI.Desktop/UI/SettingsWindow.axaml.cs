using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BluerCurve.Chrome;

namespace Waylonia.UI;

internal sealed partial class SettingsWindow : BluerCurveWindow
{
    private bool _allowClose;

    public SettingsWindow()
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

    public SettingsWindow(SettingsViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
    }

    public SettingsViewModel Model => (SettingsViewModel)DataContext!;

    public SettingsView View => (SettingsView)Content!;

    public void AllowClose() => _allowClose = true;
}
