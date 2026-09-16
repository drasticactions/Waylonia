using Waylonia.Sessions;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionMenuTests
{
    private static SessionMenuEntry Entry(
        string name,
        SessionStatus status,
        IReadOnlyList<ApplicationMenuItem>? applications = null,
        string? notice = null,
        List<string>? calls = null)
    {
        calls ??= [];
        return new SessionMenuEntry(
            name, status, applications, notice,
            () => calls.Add($"connect {name}"),
            () => calls.Add($"disconnect {name}"),
            () => calls.Add($"refresh {name}"));
    }

    [Fact]
    public void The_manager_item_comes_first_and_each_session_is_a_submenu_with_a_status_glyph()
    {
        var opened = false;
        var items = SessionMenu.Build(
            [Entry("dev", SessionStatus.Connected), Entry("lab", SessionStatus.Disconnected), Entry("box", SessionStatus.Connecting)],
            null, null, null, () => opened = true);

        Assert.Equal(SessionMenu.ManagerLabel, items[0].Label);
        items[0].Invoke!();
        Assert.True(opened);
        Assert.True(items[1].Separator);
        Assert.Equal(["● dev", "○ lab", "… box"], items.Skip(2).Take(3).Select(item => item.Label));
        Assert.True(items[5].Separator);
        Assert.Equal(6, items.Count);
    }

    [Fact]
    public void The_settings_item_follows_the_manager_item_and_stands_alone_without_it()
    {
        var opened = new List<string>();
        var items = SessionMenu.Build(
            [Entry("dev", SessionStatus.Connected)], null, null, null, () => opened.Add("manager"), () => opened.Add("settings"));

        Assert.Equal([SessionMenu.ManagerLabel, SessionMenu.SettingsLabel], items.Take(2).Select(item => item.Label));
        items[1].Invoke!();
        Assert.Equal(["settings"], opened);
        Assert.True(items[2].Separator);
        Assert.Equal("● dev", items[3].Label);

        var alone = SessionMenu.Build([], null, null, null, null, () => opened.Add("settings"));
        Assert.Equal(SessionMenu.SettingsLabel, alone[0].Label);
        Assert.True(alone[1].Separator);
        Assert.Equal(2, alone.Count);
    }

    [Fact]
    public void A_disconnected_session_offers_connect_and_nothing_else()
    {
        var calls = new List<string>();
        var items = SessionMenu.Build([Entry("lab", SessionStatus.Disconnected, calls: calls)], null, null, null, null);

        var connect = Assert.Single(items[0].Children!);
        Assert.Equal("Connect", connect.Label);
        connect.Invoke!();
        Assert.Equal(["connect lab"], calls);
    }

    [Fact]
    public void A_connected_session_lists_its_applications_tagged_with_the_session_and_a_refresh()
    {
        var calls = new List<string>();
        var apps = new[]
        {
            new ApplicationMenuItem("Internet", Children:
            [
                new ApplicationMenuItem("Firefox", Children:
                [
                    new ApplicationMenuItem("Firefox", "firefox"),
                    new ApplicationMenuItem(string.Empty, Separator: true),
                    new ApplicationMenuItem("Private", "firefox --private"),
                ]),
            ]),
        };
        var items = SessionMenu.Build([Entry("dev", SessionStatus.Connected, apps, calls: calls)], null, null, null, null);

        var children = items[0].Children!;
        Assert.Equal("Disconnect", children[0].Label);
        children[0].Invoke!();
        Assert.True(children[1].Separator);
        Assert.Equal("Internet", children[2].Label);
        var firefox = children[2].Children![0].Children!;
        Assert.Equal(("firefox", "dev"), (firefox[0].Command, firefox[0].Session));
        Assert.Equal(("firefox --private", "dev"), (firefox[2].Command, firefox[2].Session));
        Assert.Null(children[2].Session);
        Assert.True(children[3].Separator);
        Assert.Equal(SessionMenu.RefreshLabel, children[4].Label);
        children[4].Invoke!();
        Assert.Equal(["disconnect dev", "refresh dev"], calls);
    }

    [Fact]
    public void A_connecting_session_shows_its_notice_without_a_refresh()
    {
        var items = SessionMenu.Build([Entry("dev", SessionStatus.Connecting, notice: "Loading applications…")], null, null, null, null);

        var children = items[0].Children!;
        Assert.Equal(["Disconnect", string.Empty, "Loading applications…"], children.Select(item => item.Label));
        Assert.Null(children[2].Command);
        Assert.Null(children[2].Invoke);
    }

    [Fact]
    public void Local_applications_stay_at_the_top_untagged()
    {
        var refreshed = false;
        var items = SessionMenu.Build(
            [Entry("dev", SessionStatus.Disconnected)],
            [new ApplicationMenuItem("Utilities", Children: [new ApplicationMenuItem("foot", "foot")])],
            null,
            () => refreshed = true,
            null);

        Assert.Equal("Utilities", items[0].Label);
        Assert.Null(items[0].Children![0].Session);
        Assert.True(items[1].Separator);
        Assert.Equal(SessionMenu.RefreshLabel, items[2].Label);
        items[2].Invoke!();
        Assert.True(refreshed);
        Assert.True(items[3].Separator);
        Assert.Equal("○ dev", items[4].Label);
    }
}
