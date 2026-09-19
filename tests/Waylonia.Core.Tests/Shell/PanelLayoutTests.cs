using Basin.Diagnostics;
using Basin.Shell.Nested;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

[Collection(LogCaptureCollection.Name)]
public sealed class PanelLayoutTests
{
    [Fact]
    public void Every_applet_name_parses_in_order()
    {
        var applets = PanelApplets.Parse(
            ["menu-bar", "window-list", "workspace-switcher", "clock", "show-desktop", "launcher:org.gnome.Nautilus", "spacer"],
            "top",
            BasinLogger.None);

        Assert.Equal(
            [
                new PanelApplet(PanelAppletKind.MenuBar),
                new PanelApplet(PanelAppletKind.WindowList),
                new PanelApplet(PanelAppletKind.WorkspaceSwitcher),
                new PanelApplet(PanelAppletKind.Clock),
                new PanelApplet(PanelAppletKind.ShowDesktop),
                new PanelApplet(PanelAppletKind.Launcher, "org.gnome.Nautilus"),
                new PanelApplet(PanelAppletKind.Spacer),
            ],
            applets);
    }

    [Fact]
    public void An_unknown_name_warns_with_the_panel_and_the_applets_and_is_skipped()
    {
        using var capture = new LogCapture();
        var applets = PanelApplets.Parse(["menu-bar", "foo", "clock"], "top", BasinLog.For("test"));

        Assert.Equal([new PanelApplet(PanelAppletKind.MenuBar), new PanelApplet(PanelAppletKind.Clock)], applets);
        Assert.Contains(
            "Warn: [panel] top: 'foo' is not an applet, skipping it; the applets are menu-bar, window-list, workspace-switcher, clock, show-desktop, launcher:NAME and spacer",
            capture.Lines);
    }

    [Fact]
    public void A_launcher_without_an_id_is_not_an_applet()
    {
        using var capture = new LogCapture();
        var applets = PanelApplets.Parse(["launcher:", "launcher: "], "bottom", BasinLog.For("test"));

        Assert.Empty(applets);
        Assert.Equal(2, capture.Lines.Count(line => line.StartsWith("Warn: [panel] bottom: 'launcher:", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_empty_list_is_an_empty_panel()
    {
        var layout = PanelArrangement.From(new PanelSettings(Top: [], Bottom: []), BasinLogger.None);

        Assert.Empty(layout.Top);
        Assert.Empty(layout.Bottom);
        Assert.Equal(PanelSettings.DefaultSize, layout.Size);
        Assert.Equal(new PanelLayout(PanelSettings.DefaultSize, 0, 0), layout.Layout);
    }

    [Fact]
    public void The_default_settings_give_the_mate_layout()
    {
        var layout = PanelArrangement.From(new PanelSettings(), BasinLogger.None);

        Assert.Equal(24, layout.Size);
        Assert.Equal(new PanelLayout(24, 1, 2), layout.Layout);
        Assert.Equal([new PanelApplet(PanelAppletKind.MenuBar)], layout.Top);
        Assert.Equal([new PanelApplet(PanelAppletKind.WindowList), new PanelApplet(PanelAppletKind.WorkspaceSwitcher)], layout.Bottom);
    }

    [Fact]
    public void Names_are_trimmed_and_case_sensitive()
    {
        Assert.Equal(new PanelApplet(PanelAppletKind.Clock), PanelApplets.ParseApplet(" clock "));
        Assert.Null(PanelApplets.ParseApplet("Clock"));
        Assert.Null(PanelApplets.ParseApplet(""));
    }
}
