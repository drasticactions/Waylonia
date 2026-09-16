using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Waylonia.Shell.Applets;

internal sealed class IconImageConverter : IValueConverter
{
    public const int Size = 16;

    public static IconImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Bitmap bitmap ? new Image { Source = bitmap, Width = Size, Height = Size } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
