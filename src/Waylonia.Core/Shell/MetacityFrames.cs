using Basin.Capabilities;
using Basin.Frames.Metacity;
using SkiaSharp;

namespace Waylonia.Shell;

internal sealed class MetacityFrames : IDisposable
{
    private readonly MetacityTheme _theme;
    private readonly MetacityPalette _palette;
    private readonly MetacityButtonLayout _layout;
    private readonly MetacityFont _font;
    private readonly MetacityResources _resources;
    private bool _disposed;

    private MetacityFrames(MetacityTheme theme, string themeName, string layoutText, string paletteName, double fontSize)
    {
        _theme = theme;
        ThemeName = themeName;
        LayoutText = layoutText;
        PaletteName = paletteName;
        FontSize = fontSize;
        _palette = paletteName == "dark" ? MetacityPalette.Dark : MetacityPalette.Light;
        _layout = MetacityButtonLayout.Parse(layoutText);
        _font = new MetacityFont(TitleTypeface(), (float)fontSize);
        _resources = new MetacityResources();
    }

    public string ThemeName { get; }

    public string LayoutText { get; }

    public string PaletteName { get; }

    public double FontSize { get; }

    public static MetacityFrames Load(ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var theme = LoadTheme(settings.Theme);
        return new MetacityFrames(theme, settings.Theme, settings.ButtonLayout, settings.Palette, settings.FontSize);
    }

    public static MetacityTheme LoadTheme(string name)
    {
        if (MetacityThemes.Find(name) is not null)
        {
            return MetacityTheme.Load(name);
        }

        if (BundledThemes.Load(name) is { } bundled)
        {
            return bundled;
        }

        throw new MetacityThemeException(
            $"no metacity theme is named '{name}'; the bundled theme is {string.Join(", ", BundledThemes.Names)} " +
            $"and the installed ones are {Installed()}");
    }

    public static string Installed()
    {
        try
        {
            var available = MetacityThemes.Available();
            return available.Count == 0 ? "none" : string.Join(", ", available);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }

    public bool Matches(ShellSettings settings) =>
        settings.Theme == ThemeName && settings.ButtonLayout == LayoutText && settings.Palette == PaletteName
        && Math.Abs(settings.FontSize - FontSize) < 0.01;

    public IFrameRenderer CreateRenderer() => new MetacityFrameRenderer(_theme, _palette, _layout, _font, _resources);

    public int TitleHeight(double scale) => _font.TextHeight(scale);

    private static SKTypeface TitleTypeface() =>
        SKFontManager.Default.MatchFamily("Sans", SKFontStyle.Bold)
        ?? SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, SKFontStyle.Bold)
        ?? SKTypeface.Default;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resources.Dispose();
        _font.Dispose();
    }
}
