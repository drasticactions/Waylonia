using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Waylonia.Shell.Applets;

namespace Waylonia.Shell;

internal sealed partial class PanelView : UserControl
{
    public const string BackgroundKey = "PanelBackgroundBrush";

    public const string ForegroundKey = "PanelForegroundBrush";

    public const string HighlightKey = "PanelHighlightBrush";

    private static readonly (string Key, string[] Sources, IBrush Fallback)[] Palette =
    [
        (BackgroundKey, ["BcBgBrush", "SystemControlBackgroundChromeMediumBrush"], Brushes.LightGray),
        (ForegroundKey, ["BcFgBrush", "SystemControlForegroundBaseHighBrush"], Brushes.Black),
        (HighlightKey, ["BcSelectedBgBrush", "SystemControlHighlightAccentBrush"], Brushes.SteelBlue),
    ];

    public PanelView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(ContextRequestedEvent, OnContextRequested);
        AddHandler(PointerReleasedEvent, OnPointerReleased);
        ActualThemeVariantChanged += (_, _) => ResolvePalette();
    }

    public PanelView(PanelViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
    }

    public PanelViewModel? Model => DataContext as PanelViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolvePalette();
        foreach (var clock in Clocks())
        {
            clock.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var clock in Clocks())
        {
            clock.Stop();
        }

        base.OnDetachedFromVisualTree(e);
    }

    private IEnumerable<ClockViewModel> Clocks() => Model?.Applets.OfType<ClockViewModel>() ?? [];

    private void ResolvePalette()
    {
        foreach (var (key, sources, fallback) in Palette)
        {
            Resources[key] = Resolve(sources) ?? fallback;
        }
    }

    private IBrush? Resolve(string[] sources)
    {
        foreach (var source in sources)
        {
            if (this.TryFindResource(source, ActualThemeVariant, out var value) && value is IBrush brush)
            {
                return brush;
            }
        }

        return null;
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Owner<ToggleButton, WindowButtonViewModel>(e.Source) is { } button)
        {
            button.MenuCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && Owner<Border, WorkspaceCellViewModel>(e.Source) is { } cell)
        {
            cell.SwitchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static TModel? Owner<TControl, TModel>(object? source)
        where TControl : Control
        where TModel : class
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is TControl { DataContext: TModel model })
            {
                return model;
            }
        }

        return null;
    }
}
