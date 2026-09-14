using Basin.Freedesktop;
using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class RemoteApplicationsTests
{
    private const string Mark = "\u0001";

    private static string Record(string root, string path, string ok, string body) =>
        $"{Mark}{root}{Mark}{path}{Mark}{ok}\n{body}\n";

    [Fact]
    public void The_script_keeps_only_the_locale_it_was_given()
    {
        var japanese = RemoteApplications.Script(DesktopLocale.Parse("ja_JP.UTF-8"));
        Assert.Contains("/^[A-Za-z][-A-Za-z0-9]*\\[/{/^[A-Za-z][-A-Za-z0-9]*\\[ja/!d;}", japanese);
        Assert.Contains("printf '\\001%s\\001%s\\001%s\\n'", japanese);
        Assert.Contains("TryExec", japanese);
        Assert.DoesNotContain("\n", japanese);

        var none = RemoteApplications.Script(DesktopLocale.None);
        Assert.Contains("-e '/^[A-Za-z][-A-Za-z0-9]*\\[/d'", none);
        Assert.DoesNotContain("!d", none);
    }

    [Fact]
    public void The_first_directory_wins_and_a_hidden_entry_deletes_the_system_one()
    {
        var output =
            Record("/home/u/.local/share/applications", "/home/u/.local/share/applications/editor.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=User Editor\nExec=editor") +
            Record("/home/u/.local/share/applications", "/home/u/.local/share/applications/gone.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=Gone\nHidden=true") +
            Record("/usr/share/applications", "/usr/share/applications/editor.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=System Editor\nExec=editor") +
            Record("/usr/share/applications", "/usr/share/applications/gone.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=Must Not Appear\nExec=gone") +
            Record("/usr/share/applications", "/usr/share/applications/kde4/kate.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=Kate\nExec=kate\nCategories=Utility;TextEditor;");

        var remote = RemoteApplications.Parse(output, DesktopLocale.None);

        Assert.Equal(["kde4-kate.desktop", "editor.desktop"], remote.Entries.Select(entry => entry.Id));
        Assert.Equal("User Editor", remote.Entries[1].Name);
        Assert.Equal("/home/u/.local/share/applications/editor.desktop", remote.Entries[1].Path);
        Assert.Equal(["Utility", "TextEditor"], remote.Entries[0].Categories);
    }

    [Fact]
    public void The_remote_answer_to_try_exec_decides_listing_not_this_machine()
    {
        var output =
            Record("/usr/share/applications", "/usr/share/applications/there.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=There\nExec=there\nTryExec=/opt/only/on/the/remote") +
            Record("/usr/share/applications", "/usr/share/applications/missing.desktop", "0",
                "[Desktop Entry]\nType=Application\nName=Missing\nExec=missing\nTryExec=sh") +
            Record("/usr/share/applications", "/usr/share/applications/only.desktop", "1",
                "[Desktop Entry]\nType=Application\nName=Only\nExec=only\nOnlyShowIn=KDE;");

        var remote = RemoteApplications.Parse(output, DesktopLocale.None);

        Assert.Equal(["Missing", "Only", "There"], remote.Entries.Select(entry => entry.Name));
        Assert.Equal(["There"], remote.Listable(null).Select(entry => entry.Name));
        Assert.Equal(["Only", "There"], remote.Listable(new HashSet<string> { "KDE" }).Select(entry => entry.Name));
    }

    [Fact]
    public void Noise_before_the_first_record_and_windows_line_ends_are_tolerated()
    {
        var output = "Welcome to the box\r\n" +
            Record("/usr/share/applications", "/usr/share/applications/a.desktop", "1",
                "[Desktop Entry]\r\nType=Application\r\nName=A\r\nName[de]=Ah\r\nExec=a\r\n");

        var remote = RemoteApplications.Parse(output, DesktopLocale.Parse("de"));

        var entry = Assert.Single(remote.Entries);
        Assert.Equal("Ah", entry.Name);
        Assert.Equal(["a"], entry.Argv);
        Assert.Empty(RemoteApplications.Parse(string.Empty, DesktopLocale.None).Entries);
    }

    [Fact]
    public void A_record_outside_its_root_is_dropped_rather_than_named_wrongly()
    {
        var output = Record("/usr/share/applications", "/elsewhere/a.desktop", "1",
            "[Desktop Entry]\nType=Application\nName=A\nExec=a");

        Assert.Empty(RemoteApplications.Parse(output, DesktopLocale.None).Entries);
    }
}
