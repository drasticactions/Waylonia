using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Waylonia.UI;

internal sealed partial class SettingsView : UserControl
{
    public const int DefaultWidth = 760;

    public const int DefaultHeight = 640;

    public SettingsView() => AvaloniaXamlLoader.Load(this);
}
