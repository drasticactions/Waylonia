using Basin.Diagnostics;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

[Collection(LogCaptureCollection.Name)]
public sealed class PanelArrangementTests
{
    private static readonly HostCapabilities Touch = HostCapabilities.None with { SoftKeyboard = true };

    [Fact]
    public void A_host_with_a_soft_keyboard_appends_the_applet_to_the_default_bottom_strip()
    {
        var arrangement = PanelArrangement.From(new PanelSettings(), Touch, BasinLogger.None);

        Assert.Equal(
            [PanelAppletKind.WindowList, PanelAppletKind.WorkspaceSwitcher, PanelAppletKind.Keyboard],
            arrangement.Bottom.Select(static applet => applet.Kind));
        Assert.Equal([PanelAppletKind.MenuBar], arrangement.Top.Select(static applet => applet.Kind));
    }

    [Fact]
    public void A_configured_bottom_strip_is_left_alone_and_a_named_keyboard_is_kept_once()
    {
        var configured = PanelArrangement.From(new PanelSettings(Bottom: ["window-list"]), Touch, BasinLogger.None);
        Assert.Equal([PanelAppletKind.WindowList], configured.Bottom.Select(static applet => applet.Kind));

        var named = PanelArrangement.From(new PanelSettings(Top: ["keyboard", "menu-bar"]), Touch, BasinLogger.None);
        Assert.Equal([PanelAppletKind.Keyboard, PanelAppletKind.MenuBar], named.Top.Select(static applet => applet.Kind));
    }

    [Fact]
    public void A_desktop_has_no_keyboard_applet_and_warns_when_the_config_names_one()
    {
        using var capture = new LogCapture();
        var plain = PanelArrangement.From(new PanelSettings(), BasinLog.For("test"));
        Assert.DoesNotContain(plain.Bottom, static applet => applet.Kind == PanelAppletKind.Keyboard);
        Assert.Empty(capture.Lines);

        var named = PanelArrangement.From(new PanelSettings(Bottom: ["keyboard", "clock"]), HostCapabilities.None, BasinLog.For("test"));
        Assert.Equal([PanelAppletKind.Clock], named.Bottom.Select(static applet => applet.Kind));
        Assert.Equal(["Warn: [panel] bottom: the keyboard applet needs a host with a soft keyboard, dropping it"], capture.Lines);
    }
}
