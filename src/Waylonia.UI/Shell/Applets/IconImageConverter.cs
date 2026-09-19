using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Waylonia.Shell.Applets;

internal sealed class IconImageConverter : IValueConverter
{
    public const int Size = 16;

    public static IconImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bitmap = value switch
        {
            Bitmap loaded => loaded,
            string path => IconImages.Load(path),
            _ => null,
        };
        if (targetType == typeof(bool))
        {
            return bitmap is not null;
        }

        if (bitmap is null)
        {
            return null;
        }

        return typeof(IImage).IsAssignableFrom(targetType) ? bitmap : new Image { Source = bitmap, Width = Size, Height = Size };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
