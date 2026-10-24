using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Waylonia.UI;

/// <summary>The form row theme and the classes the manager, the settings and the dialogs share.</summary>
internal sealed partial class FormStyles : Styles
{
    public FormStyles() => AvaloniaXamlLoader.Load(this);
}
