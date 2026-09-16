using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Waylonia.Ui;

internal sealed partial class SettingsWindow : Window
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

    public void AllowClose() => _allowClose = true;
}
