using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace Waylonia.Shell.Applets;

internal static class IconImages
{
    public const int RasterSize = 48;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Sync = new();

    public static Bitmap? Load(string? path)
    {
        if (path is not { Length: > 0 })
        {
            return null;
        }

        lock (Sync)
        {
            if (Cache.TryGetValue(path, out var known))
            {
                return known;
            }
        }

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? FromSvg(path) : new Bitmap(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Xml.XmlException)
        {
            bitmap = null;
        }

        lock (Sync)
        {
            Cache[path] = bitmap;
        }

        return bitmap;
    }

    private static Bitmap? FromSvg(string path)
    {
        using var svg = new SKSvg();
        if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            return null;
        }

        var bounds = picture.CullRect;
        using var raster = new SKBitmap(new SKImageInfo(RasterSize, RasterSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(raster))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(RasterSize / bounds.Width, RasterSize / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(raster);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
