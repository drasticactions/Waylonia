using Waylonia;
using Waylonia.Sessions;
using Xunit;

using Basin.Diagnostics;

namespace Waylonia.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-tests-" + Guid.NewGuid().ToString("n"));

    private string Write(string toml)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        File.WriteAllText(path, toml);
        return path;
    }

    private static Config Load(string path) => Config.Load(false, path, BasinLogger.None);

    [Fact]
    public void Every_top_level_key_reaches_the_config()
    {
        var config = Load(Write("""
            compress = "none"
            socket = "wayland-9"
            command = "foot"
            """));

        Assert.Equal("none", config.Compress);
        Assert.Equal("wayland-9", config.Socket);
        Assert.Equal("foot", config.Command);
    }

    [Fact]
    public void Audio_is_off_unless_the_file_turns_it_on()
    {
        Assert.Null(Load(Write("compress = \"none\"")).Audio);
        Assert.True(Load(Write("audio = true")).Audio);
        Assert.False(Load(Write("audio = false")).Audio);
    }

    [Fact]
    public void A_command_array_joins_into_one_line()
    {
        var config = Load(Write("""
            command = ["tmux", "new -A", "-s main"]
            """));

        Assert.Equal("tmux new -A -s main", config.Command);
    }

    [Fact]
    public void An_unknown_compression_keeps_the_default()
    {
        var config = Load(Write("""
            compress = "brotli"
            """));

        Assert.Null(config.Compress);
    }

    [Theory]
    [InlineData("lz4")]
    [InlineData("zstd")]
    [InlineData("none")]
    public void Every_compression_the_channel_carries_is_read(string name)
    {
        var config = Load(Write($"""
            compress = "{name}"
            """));

        Assert.Equal(name, config.Compress);
    }

    [Fact]
    public void The_tray_applications_settings_read_at_the_top_level()
    {
        var config = Load(Write("""
            terminal = ["foot", "-e"]
            current-desktop = "GNOME"

            [host]
            tray-apps = false
            """));

        Assert.False(config.TrayApps);
        Assert.Equal("foot -e", config.Terminal);
        Assert.Equal("GNOME", config.CurrentDesktop);
        Assert.True(Load(Write("compress = \"none\"")).TrayApps);
        Assert.Null(Load(Write("compress = \"none\"")).Terminal);
    }

    [Fact]
    public void Lang_defaults_to_C_UTF8_and_reads_at_the_top_level()
    {
        Assert.Equal("C.UTF-8", Load(Write("compress = \"none\"")).Lang);
        Assert.Equal("en_US.UTF-8", Load(Write("lang = \"en_US.UTF-8\"")).Lang);
        Assert.Equal("", Load(Write("lang = \"\"")).Lang);
        Assert.Equal("C.UTF-8", Load(Write("lang = \"C.UTF-8; rm -rf /\"")).Lang);
    }

    [Fact]
    public void Session_titles_default_on_and_turn_off_under_host()
    {
        Assert.True(Load(Write("compress = \"none\"")).SessionTitles);
        Assert.False(Load(Write("[host]\nsession-titles = false")).SessionTitles);
    }

    [Fact]
    public void The_sessions_directory_sits_beside_the_config_file()
    {
        var config = Load(Write("compress = \"none\""));

        Assert.Equal(Path.Combine(_directory, "sessions"), config.SessionsDirectory);
        Assert.Null(Config.Load(true, null, BasinLogger.None).SessionsDirectory);
    }

    [Fact]
    public void The_host_toggles_default_on_and_turn_off_individually()
    {
        var defaults = Load(Write("socket = \"wayland-1\""));
        Assert.True(defaults.XWayland);
        Assert.True(defaults.Tray);
        Assert.True(defaults.Clipboard);
        Assert.True(defaults.Drag);
        Assert.True(defaults.FollowCursor);
        Assert.True(defaults.GtkDpi);

        var config = Load(Write("""
            [host]
            xwayland = false
            drag = false
            follow-cursor = false
            gtk-dpi = false
            """));

        Assert.False(config.XWayland);
        Assert.True(config.Tray);
        Assert.True(config.Clipboard);
        Assert.False(config.Drag);
        Assert.False(config.FollowCursor);
        Assert.False(config.GtkDpi);
    }

    [Fact]
    public void A_legacy_host_profile_is_kept_for_migration_and_warned_about()
    {
        using var capture = new LogCapture();
        var warnings = capture.Lines;
        var config = Config.Load(false, Write("""
            [hosts.dev]
            ssh = "user@devbox"
            command = "tmux new -A -s main"
            compress = "none"
            lang = "ja_JP.UTF-8"

            [hosts.broken]
            command = "foot"
            """), BasinLog.For("test"));

        var profile = Assert.Single(config.LegacyHosts);
        Assert.Equal("dev", profile.Name);
        Assert.Equal("user@devbox", profile.Ssh);
        Assert.Equal("tmux new -A -s main", profile.Command);
        Assert.Equal("none", profile.Compress);
        Assert.Equal("ja_JP.UTF-8", profile.Lang);
        Assert.Contains(warnings, line => line.Contains("[hosts.dev] moved to sessions/dev.toml", StringComparison.Ordinal));
        Assert.Contains(warnings, line => line.Contains("[hosts.broken] cannot become a session", StringComparison.Ordinal));
    }

    [Fact]
    public void A_legacy_host_migrates_into_the_sessions_directory()
    {
        var config = Load(Write("""
            [hosts.dev]
            ssh = "user@devbox"
            terminal = "xdg-terminal-exec"
            """));
        var store = new SessionStore(config.SessionsDirectory);

        store.Save(config.LegacyHosts[0]);

        var reloaded = store.Load(BasinLogger.None);
        var session = Assert.Single(reloaded.Profiles);
        Assert.Equal("dev", session.Name);
        Assert.Equal("user@devbox", session.Ssh);
        Assert.Equal("xdg-terminal-exec", session.Terminal);
        Assert.True(File.Exists(Path.Combine(_directory, "sessions", "dev.toml")));
    }

    [Fact]
    public void An_unknown_section_leaves_the_rest_of_the_file_standing()
    {
        var config = Load(Write("""
            socket = "wayland-9"

            [devbox]
            ssh = "user@devbox"
            """));

        Assert.Equal("wayland-9", config.Socket);
        Assert.Empty(config.LegacyHosts);
    }

    [Fact]
    public void A_file_that_does_not_parse_keeps_every_default()
    {
        var config = Load(Write("compress = \"none"));

        Assert.Null(config.Compress);
        Assert.Null(config.Socket);
        Assert.True(config.XWayland);
    }

    [Fact]
    public void An_explicit_path_that_is_missing_writes_nothing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "absent.toml");
        var config = Config.Load(false, path, BasinLogger.None);

        Assert.False(File.Exists(path));
        Assert.Null(config.Socket);
    }

    [Fact]
    public void Skipping_the_file_ignores_one_that_is_there()
    {
        var path = Write("""
            socket = "wayland-9"
            compress = "none"
            """);

        var config = Config.Load(true, path, BasinLogger.None);

        Assert.Null(config.Socket);
        Assert.Null(config.Compress);
    }

    [Fact]
    public void The_default_path_gets_a_placeholder_that_parses_back_to_the_defaults()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _directory);
        try
        {
            var config = Config.Load(false, null, BasinLogger.None);
            var path = Path.Combine(_directory, "waylonia", "waylonia.toml");

            Assert.True(File.Exists(path));
            Assert.Null(config.Socket);

            var reloaded = Config.Load(false, null, BasinLogger.None);
            Assert.Null(reloaded.Socket);
            Assert.Null(reloaded.Compress);
            Assert.Null(reloaded.Command);
            Assert.True(reloaded.XWayland);
            Assert.True(reloaded.SessionTitles);
            Assert.Empty(reloaded.LegacyHosts);
            Assert.Empty(reloaded.Hotkeys);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void A_hotkey_table_becomes_parsed_chords()
    {
        var config = Load(Write("""
            [hotkeys]
            "ctrl+alt+t" = "foot"
            "super+shift+return" = ["foot", "-e", "htop"]
            """));

        Assert.Equal(2, config.Hotkeys.Count);
        var terminal = Assert.Single(config.Hotkeys, hotkey => hotkey.Chord == "ctrl+alt+t");
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, terminal.Modifiers);
        Assert.Equal("t", terminal.Key);
        Assert.Equal("foot", terminal.Command);

        var other = Assert.Single(config.Hotkeys, hotkey => hotkey.Chord == "super+shift+return");
        Assert.Equal(HotkeyModifiers.Super | HotkeyModifiers.Shift, other.Modifiers);
        Assert.Equal("foot -e htop", other.Command);
    }

    [Fact]
    public void A_hotkey_without_a_command_is_dropped()
    {
        var config = Load(Write("""
            [hotkeys]
            "ctrl+alt+t" = ""
            "ctrl+alt+u" = "foot"
            """));

        var kept = Assert.Single(config.Hotkeys);
        Assert.Equal("ctrl+alt+u", kept.Chord);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
