using Basin.Diagnostics;
using Waylonia.Cli;
using Xunit;

namespace Waylonia.Tests;

public sealed class ConfigWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-writer-" + Guid.NewGuid().ToString("n"));

    private string PathOf(string? text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        return path;
    }

    private static string Apply(string text, ConfigValues values)
    {
        var document = TomlDocument.Parse(text, out var error);
        Assert.Null(error);
        ConfigWriter.Apply(document!, values);
        return document!.Render();
    }

    [Fact]
    public void Values_at_their_defaults_write_nothing()
    {
        var document = TomlDocument.Empty();
        ConfigWriter.Apply(document, new ConfigValues());
        Assert.Equal(string.Empty, document.Render());
    }

    [Fact]
    public void Every_value_lands_in_the_file_and_loads_back()
    {
        var path = PathOf(null);
        var values = new ConfigValues(
            "zstd", true, false, "h264,hw", "wayland-9", "foot", "foot -e", "KDE", "en_US.UTF-8",
            false, false, false, false, false, false, false, false, "RightAlt",
            [Hotkey.Parse("ctrl+alt+t", "foot", BasinLogger.None)!],
            [new DesktopProfile("lab", "sway", "devbox", "1920x1080", "sway --unsupported-gpu", ["A=1", "B=2"], false, "none")]);

        Assert.Null(ConfigWriter.Save(path, values));

        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal("zstd", config.Compress);
        Assert.True(config.Gpu);
        Assert.False(config.Audio);
        Assert.Equal("h264,hw", config.Video);
        Assert.Equal("wayland-9", config.Socket);
        Assert.Equal("foot", config.Command);
        Assert.Equal("foot -e", config.Terminal);
        Assert.Equal("KDE", config.CurrentDesktop);
        Assert.Equal("en_US.UTF-8", config.Lang);
        Assert.False(config.XWayland);
        Assert.False(config.Tray);
        Assert.False(config.TrayApps);
        Assert.False(config.Clipboard);
        Assert.False(config.Drag);
        Assert.False(config.FollowCursor);
        Assert.False(config.GtkDpi);
        Assert.False(config.SessionTitles);
        Assert.Equal("RightAlt", config.CaptureChord);
        var hotkey = Assert.Single(config.Hotkeys);
        Assert.Equal(("ctrl+alt+t", "foot"), (hotkey.Chord, hotkey.Command));
        var desktop = Assert.Single(config.Desktops.Values);
        Assert.Equal(values.Desktops[0], desktop with { Env = values.Desktops[0].Env });
        Assert.Equal(["A=1", "B=2"], desktop.Env);
    }

    [Fact]
    public void An_empty_lang_is_written_as_an_empty_string_and_the_default_is_dropped()
    {
        Assert.Equal("lang = \"\"\n", Apply(string.Empty, new ConfigValues(Lang: string.Empty)));
        Assert.Equal(string.Empty, Apply("lang = \"C.UTF-8\"\n", new ConfigValues()));
    }

    [Fact]
    public void A_toggle_back_at_its_default_loses_its_key_and_the_chord_too()
    {
        var text = Apply("[host]\nclipboard = false\ncapture-chord = \"RightAlt\"\n", new ConfigValues());
        Assert.Equal("[host]\n", text);
    }

    [Fact]
    public void The_placeholder_keeps_its_comments_and_the_new_keys_load_back()
    {
        var path = PathOf(null);
        Config.WritePlaceholder(path, BasinLogger.None);
        var placeholder = File.ReadAllText(path);

        Assert.Null(ConfigWriter.Save(path, new ConfigValues(Compress: "none", Tray: false)));

        var text = File.ReadAllText(path);
        Assert.StartsWith(placeholder, text, StringComparison.Ordinal);
        Assert.EndsWith("compress = \"none\"\n\n[host]\ntray = false\n", text, StringComparison.Ordinal);
        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal("none", config.Compress);
        Assert.False(config.Tray);
    }

    [Fact]
    public void A_hotkey_dropped_from_the_list_goes_and_the_others_keep_their_comments()
    {
        var text = Apply(
            "[hotkeys]\n# terminal\n\"ctrl+alt+t\" = \"foot\"\n# browser\n\"ctrl+alt+b\" = [\"firefox\"]\n",
            new ConfigValues(Hotkeys:
            [
                Hotkey.Parse("ctrl+alt+b", "firefox", BasinLogger.None)!,
                Hotkey.Parse("ctrl+alt+m", "thunderbird", BasinLogger.None)!,
            ]));

        Assert.Equal(
            "[hotkeys]\n# terminal\n# browser\n\"ctrl+alt+b\" = [\"firefox\"]\n\"ctrl+alt+m\" = \"thunderbird\"\n",
            text);
    }

    [Fact]
    public void A_command_array_is_left_alone_while_its_text_is_unchanged()
    {
        const string text = "command = [\"tmux\", \"new\"]\nterminal = \"foot -e\"\n";
        Assert.Equal(text, Apply(text, new ConfigValues(Command: "tmux new", Terminal: "foot -e")));
        Assert.Equal("command = \"tmux attach\"\nterminal = \"foot -e\"\n", Apply(text, new ConfigValues(Command: "tmux attach", Terminal: "foot -e")));
    }

    [Fact]
    public void A_desktop_dropped_from_the_list_loses_its_table_and_an_edited_one_keeps_its_place()
    {
        var text = Apply(
            "[desktops.lab]\nrecipe = \"sway\"\n\n[desktops.kde]\n# big\nsize = \"1920x1080\"\nrecipe = \"plasma\"\nenv = [\"A=1\"]\n",
            new ConfigValues(Desktops: [new DesktopProfile("kde", "plasma", "devbox", null, null, [], true, null)]));

        Assert.Equal("[desktops.kde]\n# big\nrecipe = \"plasma\"\nhost = \"devbox\"\ngpu = true\n", text);
    }

    [Fact]
    public void A_broken_file_is_reported_and_left_alone()
    {
        var path = PathOf("compress = \n");
        var error = ConfigWriter.Save(path, new ConfigValues(Compress: "none"));

        Assert.NotNull(error);
        Assert.Contains("did not parse", error, StringComparison.Ordinal);
        Assert.Equal("compress = \n", File.ReadAllText(path));
    }

    [Fact]
    public void Restart_keys_are_the_ones_the_host_reads_once()
    {
        var running = new ConfigValues();
        Assert.Empty(running.RestartKeysChanged(new ConfigValues(SessionTitles: false, Hotkeys: [Hotkey.Parse("ctrl+t", "foot", BasinLogger.None)!])));
        Assert.Equal(
            ["xwayland", "tray", "clipboard", "drag", "follow-cursor", "socket", "command"],
            running.RestartKeysChanged(new ConfigValues(
                XWayland: false, Tray: false, Clipboard: false, Drag: false, FollowCursor: false, Socket: "wayland-9", Command: "foot")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
