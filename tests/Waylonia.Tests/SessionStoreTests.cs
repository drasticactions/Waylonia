using Basin.Diagnostics;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-sessions-" + Guid.NewGuid().ToString("n"));

    private SessionStore Store => new(_directory);

    [Fact]
    public void Every_key_survives_a_round_trip()
    {
        var profile = new SessionProfile(
            "dev",
            "user@devbox",
            "tmux new -A -s main",
            ["foot", "firefox --new-window \"a b\""],
            "none",
            true,
            "h264,hw",
            true,
            "xdg-terminal-exec",
            "KDE",
            "en_US.UTF-8",
            Autoconnect: true,
            Desktop: "plasma",
            DesktopSize: "1920x1080",
            DesktopEnv: ["QT_QPA_PLATFORM=wayland"],
            Hotkeys: [Hotkey.Parse("ctrl+alt+t", "foot", BasinLogger.None, "dev")!]);

        Store.Save(profile);
        var catalog = Store.Load(BasinLogger.None);

        Assert.Empty(catalog.Broken);
        var read = Assert.Single(catalog.Profiles);
        Assert.Equal(
            profile with { Hotkeys = null, Autostart = null, DesktopEnv = null },
            read with { Hotkeys = null, Autostart = null, DesktopEnv = null });
        var hotkey = Assert.Single(read.Hotkeys!);
        Assert.Equal(("ctrl+alt+t", "foot", "dev"), (hotkey.Chord, hotkey.Command, hotkey.Session));
        Assert.Equal(["foot", "firefox --new-window \"a b\""], read.Autostart!);
        Assert.Equal(["QT_QPA_PLATFORM=wayland"], read.DesktopEnv!);
    }

    [Fact]
    public void A_bare_profile_writes_only_its_ssh_line()
    {
        Assert.Equal("ssh = \"user@host\"\n", SessionStore.Render(new SessionProfile("x", "user@host")));
    }

    [Fact]
    public void An_empty_lang_means_leave_the_remote_alone_and_a_missing_one_means_the_config_default()
    {
        Store.Save(new SessionProfile("bare", "user@bare", Lang: string.Empty));
        Store.Save(new SessionProfile("plain", "user@plain"));

        var catalog = Store.Load(BasinLogger.None);

        Assert.Equal(string.Empty, catalog.Find("bare")!.Lang);
        Assert.Null(catalog.Find("plain")!.Lang);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("lab.home")]
    [InlineData("box_2-b")]
    public void A_safe_name_is_accepted(string name)
    {
        Assert.True(SessionStore.IsValidName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("user@host")]
    [InlineData("../etc")]
    [InlineData("a b")]
    [InlineData(".")]
    [InlineData("..")]
    public void A_name_that_is_unsafe_in_a_socket_path_is_rejected(string name)
    {
        Assert.False(SessionStore.IsValidName(name));
        Assert.Throws<ArgumentException>(() => Store.Save(new SessionProfile(name, "user@host")));
    }

    [Fact]
    public void A_file_that_does_not_parse_is_listed_as_broken_with_the_message()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "bad.toml"), "ssh = \"user@host");
        File.WriteAllText(Path.Combine(_directory, "nossh.toml"), "command = \"foot\"");
        File.WriteAllText(Path.Combine(_directory, "good.toml"), "ssh = \"user@host\"");

        var catalog = Store.Load(BasinLogger.None);

        Assert.Equal("good", Assert.Single(catalog.Profiles).Name);
        Assert.Equal(2, catalog.Broken.Count);
        Assert.Contains(catalog.Broken, broken => broken.Name == "bad" && broken.Error.Length > 0);
        Assert.Contains(catalog.Broken, broken => broken.Name == "nossh" && broken.Error.Contains("ssh", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_video_or_compression_or_desktop_breaks_the_file_rather_than_running_with_a_guess()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "video.toml"), "ssh = \"h\"\nvideo = \"mpeg\"");
        File.WriteAllText(Path.Combine(_directory, "compress.toml"), "ssh = \"h\"\ncompress = \"brotli\"");
        File.WriteAllText(Path.Combine(_directory, "desktop.toml"), "ssh = \"h\"\ndesktop = \"gnome\"");

        var catalog = Store.Load(BasinLogger.None);

        Assert.Empty(catalog.Profiles);
        Assert.Equal(["compress", "desktop", "video"], catalog.Broken.Select(broken => broken.Name).Order());
    }

    [Fact]
    public void Delete_removes_the_file_and_reports_whether_it_was_there()
    {
        Store.Save(new SessionProfile("dev", "user@devbox"));

        Assert.True(Store.Delete("dev"));
        Assert.False(Store.Delete("dev"));
        Assert.Empty(Store.Load(BasinLogger.None).Profiles);
    }

    [Fact]
    public void A_store_without_a_directory_loads_nothing_and_refuses_to_save()
    {
        var store = new SessionStore(null);

        Assert.Empty(store.Load(BasinLogger.None).Profiles);
        Assert.Throws<InvalidOperationException>(() => store.Save(new SessionProfile("dev", "user@devbox")));
        Assert.Null(SessionStore.DirectoryFor(null));
        Assert.Equal(Path.Combine(_directory, "sessions"), SessionStore.DirectoryFor(Path.Combine(_directory, "waylonia.toml")));
    }

    [Fact]
    public void Quoting_escapes_what_toml_needs()
    {
        Assert.Equal("\"a \\\"b\\\" \\\\ c\\n\"", SessionStore.Quote("a \"b\" \\ c\n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
