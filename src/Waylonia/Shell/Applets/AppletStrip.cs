using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace Waylonia.Shell.Applets;

internal sealed class AppletStrip : Panel
{
    public static bool Expands(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var item = child is ContentPresenter presenter ? presenter.Content : child.DataContext;
        return item is IExpandingApplet;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double fixedWidth = 0;
        double height = 0;
        var expanding = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (Expands(child))
            {
                expanding++;
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            fixedWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var share = Share(availableSize.Width, fixedWidth, expanding);
        double expandingWidth = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible || !Expands(child))
            {
                continue;
            }

            child.Measure(new Size(share, availableSize.Height));
            expandingWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var width = expanding > 0 && !double.IsInfinity(availableSize.Width)
            ? availableSize.Width
            : fixedWidth + expandingWidth;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double fixedWidth = 0;
        var expanding = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (Expands(child))
            {
                expanding++;
            }
            else
            {
                fixedWidth += child.DesiredSize.Width;
            }
        }

        var share = Share(finalSize.Width, fixedWidth, expanding);
        double x = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var width = Expands(child) ? share : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    private static double Share(double available, double fixedWidth, int expanding)
    {
        if (expanding == 0)
        {
            return 0;
        }

        return double.IsInfinity(available) ? double.PositiveInfinity : Math.Max(0, available - fixedWidth) / expanding;
    }
}
