using SkiaSharp;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ArgbIconTests
{
    [Fact]
    public void The_png_keeps_the_pixels_and_their_alpha()
    {
        uint[] argb = [0xFF3366AA, 0x80FF0000, 0x00000000, 0xFFFFFFFF];
        var png = ArgbIcon.Png(2, 2, argb);
        Assert.NotNull(png);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        Assert.Equal(new SKColor(0x33, 0x66, 0xAA, 0xFF), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0x80), bitmap.GetPixel(1, 0));
        Assert.Equal(0, bitmap.GetPixel(0, 1).Alpha);
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF, 0xFF), bitmap.GetPixel(1, 1));
    }

    [Fact]
    public void A_short_or_empty_icon_is_refused()
    {
        Assert.Null(ArgbIcon.Png(0, 0, []));
        Assert.Null(ArgbIcon.Png(2, 2, new uint[3]));
    }

    [Fact]
    public void The_stem_names_the_class_and_changes_with_the_pixels()
    {
        uint[] one = [1, 2, 3, 4];
        uint[] two = [1, 2, 3, 5];
        var first = ArgbIcon.Stem("Gimp Toolbox", one);
        Assert.StartsWith("Gimp_Toolbox-", first);
        Assert.Equal(first, ArgbIcon.Stem("Gimp Toolbox", one));
        Assert.NotEqual(first, ArgbIcon.Stem("Gimp Toolbox", two));
    }
}
