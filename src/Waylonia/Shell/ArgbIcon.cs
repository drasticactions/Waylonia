using System.Security.Cryptography;
using SkiaSharp;

namespace Waylonia.Shell;

internal static class ArgbIcon
{
    public static string Stem(string name, ReadOnlySpan<uint> argb)
    {
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(argb);
        var digest = SHA256.HashData(bytes);
        return IconCache.Safe(name) + "-" + Convert.ToHexStringLower(digest.AsSpan(0, 8));
    }

    public static byte[]? Png(int width, int height, ReadOnlySpan<uint> argb)
    {
        if (width <= 0 || height <= 0 || argb.Length < width * height)
        {
            return null;
        }

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        unsafe
        {
            var pixels = (uint*)bitmap.GetPixels();
            for (var i = 0; i < width * height; i++)
            {
                pixels[i] = argb[i];
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}
