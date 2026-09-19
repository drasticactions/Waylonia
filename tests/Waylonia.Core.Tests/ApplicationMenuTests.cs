using Basin.Freedesktop;
using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class ApplicationMenuTests
{
    private static DesktopEntry Entry(string id, string body) =>
        DesktopEntryReader.ParseText("[Desktop Entry]\nType=Application\n" + body, "/apps/" + id, id, DesktopLocale.None)!;

    [Fact]
    public void Applications_are_grouped_by_main_category_in_the_registered_order_with_other_last()
    {
        var items = ApplicationMenu.Build(
        [
            Entry("z.desktop", "Name=Zed\nExec=zed\nCategories=Development;IDE;"),
            Entry("x.desktop", "Name=Xterm\nExec=xterm\nCategories=Custom;"),
            Entry("f.desktop", "Name=Firefox\nExec=firefox %u\nCategories=Network;WebBrowser;"),
            Entry("a.desktop", "Name=Ardour\nExec=ardour\nCategories=Audio;"),
            Entry("b.desktop", "Name=Bash\nExec=bash\nCategories=Development;"),
        ], terminal: null);

        Assert.Equal(["Sound & Video", "Programming", "Internet", "Other"], items.Select(item => item.Label));
        Assert.All(items, item => Assert.Null(item.Command));
        Assert.Equal(["Bash", "Zed"], items[1].Children!.Select(item => item.Label));
        Assert.Equal("firefox", Assert.Single(items[2].Children!).Command);
    }

    [Fact]
    public void A_terminal_application_is_left_out_until_a_terminal_is_configured()
    {
        DesktopEntry[] entries = [Entry("htop.desktop", "Name=htop\nExec=htop --tree\nTerminal=true\nCategories=System;")];

        Assert.Empty(ApplicationMenu.Build(entries, terminal: null));

        var wrapped = ApplicationMenu.Build(entries, ApplicationMenu.Terminal("foot -e"));
        var item = Assert.Single(Assert.Single(wrapped).Children!);
        Assert.Equal("foot -e htop --tree", item.Command);
    }

    [Fact]
    public void Actions_open_a_submenu_with_the_application_first()
    {
        var items = ApplicationMenu.Build(
        [
            Entry("ff.desktop", "Name=Firefox\nExec=firefox %u\nCategories=Network;\nActions=new-window;private;empty;\n" +
                "[Desktop Action new-window]\nName=New Window\nExec=firefox --new-window\n" +
                "[Desktop Action private]\nName=Private Window\nExec=firefox --private-window %u\n" +
                "[Desktop Action empty]\nName=Nothing\nExec=%U\n"),
        ], terminal: null);

        var firefox = Assert.Single(Assert.Single(items).Children!);
        Assert.Equal("Firefox", firefox.Label);
        Assert.Null(firefox.Command);
        var children = firefox.Children!;
        Assert.Equal(("Firefox", "firefox"), (children[0].Label, children[0].Command));
        Assert.True(children[1].Separator);
        Assert.Equal(("New Window", "firefox --new-window"), (children[2].Label, children[2].Command));
        Assert.Equal(("Private Window", "firefox --private-window"), (children[3].Label, children[3].Command));
        Assert.Equal(4, children.Count);
    }

    [Fact]
    public void Terminal_actions_are_wrapped_like_their_application()
    {
        var entry = Entry("vim.desktop", "Name=Vim\nExec=vim %F\nTerminal=true\nActions=ro;\n[Desktop Action ro]\nName=Read only\nExec=vim -R %F\n");

        var items = ApplicationMenu.Build([entry], ["xdg-terminal-exec"]);

        var vim = Assert.Single(Assert.Single(items).Children!);
        Assert.Equal("xdg-terminal-exec vim", vim.Children![0].Command);
        Assert.Equal("xdg-terminal-exec vim -R", vim.Children[2].Command);
    }

    [Fact]
    public void A_working_directory_becomes_a_cd_in_front_of_the_command()
    {
        var entry = Entry("game.desktop", "Name=Game\nExec=\"./run game\" --fullscreen\nPath=/opt/my game\n");

        Assert.Equal("cd \"/opt/my game\" && exec \"./run game\" --fullscreen", ApplicationMenu.CommandFor(entry, null));
    }

    [Fact]
    public void The_terminal_and_current_desktop_settings_split_as_their_specs_say()
    {
        Assert.Null(ApplicationMenu.Terminal(null));
        Assert.Null(ApplicationMenu.Terminal("   "));
        Assert.Equal(["foot", "-e"], ApplicationMenu.Terminal("foot -e")!);
        Assert.Equal(["/opt/My Term/bin/term", "-e"], ApplicationMenu.Terminal("\"/opt/My Term/bin/term\" -e")!);
        Assert.Null(ApplicationMenu.CurrentDesktop(null));
        Assert.Null(ApplicationMenu.CurrentDesktop(":"));
        Assert.Equal(new HashSet<string> { "KDE", "GNOME" }, ApplicationMenu.CurrentDesktop("KDE: GNOME"));
    }
}
