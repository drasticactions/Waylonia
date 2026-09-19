using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Waylonia.Shell;

internal sealed class SwitcherView : Border
{
    public const int PanelWidth = 360;

    public const int RowHeight = 28;

    public const int Inset = 12;

    private readonly StackPanel _rows = new() { Spacing = 2 };

    public SwitcherView()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2E, 0x34, 0x36));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0x66, 0x68));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(Inset / 2.0);
        Child = _rows;
    }

    public static int HeightFor(int entries) => (entries * (RowHeight + 2)) + Inset;

    public void Show(IReadOnlyList<SwitcherEntry> entries)
    {
        _rows.Children.Clear();
        foreach (var entry in entries)
        {
            _rows.Children.Add(new SwitcherRow(entry));
        }
    }

    private sealed class SwitcherRow : Border
    {
        private static readonly IBrush Selected = new SolidColorBrush(Color.FromArgb(0xFF, 0x3D, 0x7B, 0xD9));

        public SwitcherRow(SwitcherEntry entry)
        {
            Height = RowHeight;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(8, 0);
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.White,
                FontSize = 13,
                Text = entry.Session is { } session ? $"{entry.Title} — {session}" : entry.Title,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (entry.Icon is { } icon)
            {
                row.Children.Add(new Image { Source = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
            }

            row.Children.Add(text);
            Child = row;
            Apply(entry.IsSelected);
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SwitcherEntry.IsSelected))
                {
                    Apply(entry.IsSelected);
                }
            };
        }

        private void Apply(bool selected) => Background = selected ? Selected : Brushes.Transparent;
    }
}
