using Basin.Diagnostics;
using Basin.Shell.Nested;

namespace Waylonia.Shell;

internal sealed record PanelArrangement(int Size, IReadOnlyList<PanelApplet> Top, IReadOnlyList<PanelApplet> Bottom)
{
    public static PanelArrangement From(PanelSettings settings, BasinLogger log) => From(settings, HostCapabilities.None, log);

    public static PanelArrangement From(PanelSettings settings, HostCapabilities capabilities, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(capabilities);
        var top = Applets(settings.Top, "top", capabilities, log);
        var bottom = Applets(settings.Bottom, "bottom", capabilities, log);
        if (capabilities.SoftKeyboard
            && ReferenceEquals(settings.Bottom, PanelSettings.DefaultBottom)
            && !bottom.Any(static applet => applet.Kind == PanelAppletKind.Keyboard))
        {
            bottom = [.. bottom, new PanelApplet(PanelAppletKind.Keyboard)];
        }

        return new PanelArrangement(settings.Size, top, bottom);
    }

    private static IReadOnlyList<PanelApplet> Applets(IReadOnlyList<string> names, string panel, HostCapabilities capabilities, BasinLogger log)
    {
        var applets = PanelApplets.Parse(names, panel, log);
        if (capabilities.SoftKeyboard || applets.All(static applet => applet.Kind != PanelAppletKind.Keyboard))
        {
            return applets;
        }

        log.Warn($"[panel] {panel}: the keyboard applet needs a host with a soft keyboard, dropping it");
        return applets.Where(static applet => applet.Kind != PanelAppletKind.Keyboard).ToArray();
    }

    public PanelLayout Layout => new(Size, Top.Count, Bottom.Count);

    public bool SameAs(PanelArrangement other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Size == other.Size && Top.SequenceEqual(other.Top) && Bottom.SequenceEqual(other.Bottom);
    }
}
