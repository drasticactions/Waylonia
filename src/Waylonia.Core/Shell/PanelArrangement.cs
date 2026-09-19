using Basin.Diagnostics;
using Basin.Shell.Nested;

namespace Waylonia.Shell;

internal sealed record PanelArrangement(int Size, IReadOnlyList<PanelApplet> Top, IReadOnlyList<PanelApplet> Bottom)
{
    public static PanelArrangement From(PanelSettings settings, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PanelArrangement(
            settings.Size,
            PanelApplets.Parse(settings.Top, "top", log),
            PanelApplets.Parse(settings.Bottom, "bottom", log));
    }

    public PanelLayout Layout => new(Size, Top.Count, Bottom.Count);

    public bool SameAs(PanelArrangement other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Size == other.Size && Top.SequenceEqual(other.Top) && Bottom.SequenceEqual(other.Bottom);
    }
}
