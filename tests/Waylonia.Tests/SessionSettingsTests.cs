using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-resolve-" + Guid.NewGuid().ToString("n"));

    private Config Load(string toml)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        File.WriteAllText(path, toml);
        return Config.Load(false, path, BasinLogger.None);
    }

    private static SessionSettings Resolve(SessionProfile profile, SessionOverrides overrides, Config config)
    {
        var resolution = SessionSettings.Resolve(profile, overrides, config, BasinLogger.None);
        Assert.Null(resolution.Error);
        return resolution.Settings!;
    }

    [Fact]
    public void A_flag_beats_the_session_file_which_beats_the_config_which_beats_the_default()
    {
        var config = Load("compress = \"zstd\"\ngpu = true\nlang = \"ja_JP.UTF-8\"\nterminal = \"foot -e\"");
        var profile = new SessionProfile("dev", "user@devbox", Compress: "none", Terminal: "xdg-terminal-exec");

        var fromDefault = Resolve(new SessionProfile("x", "h"), new SessionOverrides(), Config.Load(true, null, BasinLogger.None));
        Assert.Equal(WaypipeCompression.Lz4, fromDefault.Compression);
        Assert.False(fromDefault.Gpu);
        Assert.Equal("C.UTF-8", fromDefault.Lang);
        Assert.Null(fromDefault.Terminal);

        var fromConfig = Resolve(new SessionProfile("x", "h"), new SessionOverrides(), config);
        Assert.Equal(WaypipeCompression.Zstd, fromConfig.Compression);
        Assert.True(fromConfig.Gpu);
        Assert.Equal("ja_JP.UTF-8", fromConfig.Lang);
        Assert.Equal("foot -e", fromConfig.Terminal);

        var fromFile = Resolve(profile, new SessionOverrides(), config);
        Assert.Equal(WaypipeCompression.None, fromFile.Compression);
        Assert.Equal("xdg-terminal-exec", fromFile.Terminal);

        var fromFlag = Resolve(profile, new SessionOverrides(Compress: "lz4", Gpu: false), config);
        Assert.Equal(WaypipeCompression.Lz4, fromFlag.Compression);
        Assert.False(fromFlag.Gpu);
    }

    [Fact]
    public void An_empty_lang_turns_the_export_off()
    {
        var settings = Resolve(new SessionProfile("x", "h", Lang: string.Empty), new SessionOverrides(), Load("lang = \"C.UTF-8\""));

        Assert.Null(settings.Lang);
    }

    [Fact]
    public void A_desktop_session_takes_the_recipe_and_its_gpu_default()
    {
        var settings = Resolve(new SessionProfile("lab", "user@lab", Desktop: "plasma", DesktopSize: "1280x720"), new SessionOverrides(), Load(""));

        Assert.Equal("plasma", settings.Desktop!.Name);
        Assert.Equal("startplasma-wayland", settings.Desktop.Command);
        Assert.True(settings.Gpu);
        Assert.Null(settings.Command);
        Assert.Equal((1280, 720), settings.DesktopSize);
    }

    [Fact]
    public void A_custom_desktop_needs_a_command_and_the_command_overrides_a_recipe()
    {
        var config = Load("");

        Assert.NotNull(SessionSettings.Resolve(new SessionProfile("lab", "h", Desktop: "custom"), new SessionOverrides(), config, BasinLogger.None).Error);
        var custom = Resolve(new SessionProfile("lab", "h", Command: "my-session", Desktop: "custom"), new SessionOverrides(), config);
        Assert.Equal("my-session", custom.Desktop!.Command);
        var overridden = Resolve(new SessionProfile("lab", "h", Command: "startplasma-wayland --replace", Desktop: "plasma"), new SessionOverrides(), config);
        Assert.Equal("startplasma-wayland --replace", overridden.Desktop!.Command);
    }

    [Fact]
    public void A_bad_desktop_size_is_an_error()
    {
        var error = SessionSettings.Resolve(new SessionProfile("lab", "h", Desktop: "sway", DesktopSize: "big"), new SessionOverrides(), Load(""), BasinLogger.None).Error;

        Assert.Contains("WxH", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Video_implies_gpu_and_asks_for_a_decoder()
    {
        var previous = SessionSettings.CreateDecoder;
        try
        {
            SessionSettings.CreateDecoder = _ => (null, "no ffmpeg here");
            var failed = SessionSettings.Resolve(new SessionProfile("x", "h", Video: "h264,hw"), new SessionOverrides(), Load(""), BasinLogger.None);

            Assert.Null(failed.Settings);
            Assert.Contains("no ffmpeg here", failed.Error, StringComparison.Ordinal);
        }
        finally
        {
            SessionSettings.CreateDecoder = previous;
        }

        var none = Resolve(new SessionProfile("x", "h", Video: "none"), new SessionOverrides(), Load("gpu = false"));
        Assert.Null(none.Video);
        Assert.False(none.Gpu);
    }

    [Fact]
    public void Ssh_NAME_resolves_a_saved_session_and_a_destination_is_an_ad_hoc_one()
    {
        var catalog = new SessionCatalog([new SessionProfile("dev", "user@devbox", Command: "tmux")], []);

        var saved = catalog.Find("dev") ?? new SessionProfile("dev", "dev");
        var adHoc = catalog.Find("user@other") ?? new SessionProfile("user@other", "user@other");

        Assert.Equal("user@devbox", saved.Ssh);
        Assert.Equal("tmux", saved.Command);
        Assert.Equal("user@other", adHoc.Ssh);
        Assert.Null(adHoc.Command);
    }

    [Fact]
    public void Desktop_NAME_matches_a_saved_desktop_session_first()
    {
        var catalog = new SessionCatalog([new SessionProfile("plasma", "user@lab", Desktop: "plasma", Compress: "none")], []);
        var overrides = new SessionOverrides();

        var (profile, error) = Program.DesktopProfileFor("plasma", null, catalog, Load(""), ref overrides);

        Assert.Null(error);
        Assert.Equal("user@lab", profile!.Ssh);
        Assert.Equal("none", profile.Compress);
        Assert.Null(overrides.Desktop);
    }

    [Fact]
    public void Desktop_NAME_then_matches_a_desktops_profile_whose_host_names_a_session()
    {
        var catalog = new SessionCatalog([new SessionProfile("lab", "user@lab", Terminal: "foot -e")], []);
        var config = Load("""
            [desktops.work]
            recipe = "plasma"
            host = "lab"
            size = "1920x1080"
            env = ["QT_QPA_PLATFORM=wayland"]
            gpu = false
            """);
        var overrides = new SessionOverrides();

        var (profile, error) = Program.DesktopProfileFor("work", null, catalog, config, ref overrides);

        Assert.Null(error);
        Assert.Equal("work@lab", profile!.Name);
        Assert.Equal("user@lab", profile.Ssh);
        Assert.Equal("plasma", profile.Desktop);
        Assert.Equal("1920x1080", profile.DesktopSize);
        Assert.Equal(["QT_QPA_PLATFORM=wayland"], profile.DesktopEnv!);
        Assert.False(profile.Gpu);
        Assert.Equal("foot -e", profile.Terminal);
    }

    [Fact]
    public void Desktop_NAME_finally_matches_a_recipe_and_pulls_the_ssh_from_the_session()
    {
        var catalog = new SessionCatalog([new SessionProfile("lab", "user@lab", Command: "tmux")], []);
        var overrides = new SessionOverrides();

        var (profile, error) = Program.DesktopProfileFor("sway", "lab", catalog, Load(""), ref overrides);

        Assert.Null(error);
        Assert.Equal("sway@lab", profile!.Name);
        Assert.Equal("user@lab", profile.Ssh);
        Assert.Equal("sway", profile.Desktop);
        Assert.Null(profile.Command);
        Assert.Equal("sway", overrides.Desktop);

        var (local, localError) = Program.DesktopProfileFor("sway", null, catalog, Load(""), ref overrides);
        Assert.Null(localError);
        Assert.Equal(string.Empty, local!.Ssh);

        var (_, unknown) = Program.DesktopProfileFor("gnome", null, catalog, Load(""), ref overrides);
        Assert.Contains("names no recipe", unknown, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
