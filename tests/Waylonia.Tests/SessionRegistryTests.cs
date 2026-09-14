using Basin.Diagnostics;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionRegistryTests
{
    private static Hotkey Key(string chord, string command, string? session = null) =>
        Hotkey.Parse(chord, command, BasinLogger.None, session)!;

    [Fact]
    public void A_second_desktop_is_refused_and_names_the_running_one()
    {
        var live = new[] { ("lab", true), ("dev", false) };

        Assert.Equal(
            "lab is already running a desktop; disconnect it before starting another",
            SessionRegistry.DesktopConflict(live, desktop: true));
        Assert.Null(SessionRegistry.DesktopConflict(live, desktop: false));
        Assert.Null(SessionRegistry.DesktopConflict([("dev", false)], desktop: true));
    }

    [Fact]
    public void The_hotkey_union_keeps_the_first_binding_of_a_chord_and_names_both_owners()
    {
        using var capture = new LogCapture();
        var global = new[] { Key("ctrl+alt+t", "foot") };
        var dev = new[] { Key("ctrl+alt+t", "kitty", "dev"), Key("ctrl+alt+f", "firefox", "dev") };
        var lab = new[] { Key("Ctrl + Alt + F", "chromium", "lab") };

        var union = SessionRegistry.HotkeyUnion(global, [dev, lab], BasinLog.For("test"));

        Assert.Equal(["foot", "firefox"], union.Select(hotkey => hotkey.Command));
        Assert.Contains(capture.Lines, line => line.Contains("session dev is already bound by the [hotkeys] table", StringComparison.Ordinal));
        Assert.Contains(capture.Lines, line => line.Contains("session lab is already bound by session dev", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, false, false, false, false)]
    public void Only_an_ad_hoc_session_that_never_had_a_client_ends_the_process_when_nothing_else_runs(
        bool adHoc, bool hadClients, bool othersLive, bool manager, bool exits)
    {
        Assert.Equal(exits, SessionRegistry.ExitsProcess(adHoc, hadClients, othersLive, manager));
    }

    [Fact]
    public void A_registered_session_is_found_by_name_and_replaced_only_while_disconnected()
    {
        var registry = new SessionRegistry(new StubSessionHost());
        var first = StubSessionHost.For("dev");
        var session = registry.Add(first);

        Assert.Same(session, registry.Get("dev"));
        Assert.Same(session, registry.Add(first with { Command = "foot" }));
        Assert.Equal("foot", registry.Get("dev")!.Settings.Command);
        Assert.Empty(registry.Live);
        Assert.Null(registry.WhyRefused(StubSessionHost.For("lab", desktop: true)));
    }
}
