using Basin.Diagnostics;

namespace Waylonia.Shell;

internal sealed record PanelLayout(int Size, IReadOnlyList<PanelApplet> Top, IReadOnlyList<PanelApplet> Bottom)
{
    public const string AppletNames = "menu-bar, window-list, workspace-switcher, clock, show-desktop, launcher:NAME and spacer";

    public static PanelLayout From(PanelSettings settings, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PanelLayout(
            settings.Size,
            ParseApplets(settings.Top, "top", log),
            ParseApplets(settings.Bottom, "bottom", log));
    }

    public static IReadOnlyList<PanelApplet> ParseApplets(IReadOnlyList<string> names, string panel, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(names);
        var applets = new List<PanelApplet>();
        foreach (var name in names)
        {
            if (ParseApplet(name) is { } applet)
            {
                applets.Add(applet);
            }
            else
            {
                log.Warn($"[panel] {panel}: '{name}' is not an applet, skipping it; the applets are {AppletNames}");
            }
        }

        return applets;
    }

    public static PanelApplet? ParseApplet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.StartsWith("launcher:", StringComparison.Ordinal))
        {
            var id = trimmed["launcher:".Length..].Trim();
            return id.Length == 0 ? null : new PanelApplet(PanelAppletKind.Launcher, id);
        }

        return trimmed switch
        {
            "menu-bar" => new PanelApplet(PanelAppletKind.MenuBar),
            "window-list" => new PanelApplet(PanelAppletKind.WindowList),
            "workspace-switcher" => new PanelApplet(PanelAppletKind.WorkspaceSwitcher),
            "clock" => new PanelApplet(PanelAppletKind.Clock),
            "show-desktop" => new PanelApplet(PanelAppletKind.ShowDesktop),
            "spacer" => new PanelApplet(PanelAppletKind.Spacer),
            _ => null,
        };
    }
}
