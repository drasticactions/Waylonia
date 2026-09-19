using Basin.Diagnostics;
using Waylonia.Sessions;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests;

[Collection(LogCaptureCollection.Name)]
public sealed class MobileRunTests : IDisposable
{
    private static readonly HostCapabilities Mobile = new(
        Tray: false,
        GlobalHotkeys: false,
        KeyboardCapture: false,
        LocalCommands: false,
        LocalDesktops: false,
        XWayland: false,
        LocalApplications: false,
        ChannelsOnly: true,
        SoftKeyboard: true,
        KeyImport: true,
        Reconnects: true);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "waylonia-mobile-" + Guid.NewGuid().ToString("n"));

    private WayloniaPaths Paths()
    {
        var documents = Path.Combine(_root, "files");
        Directory.CreateDirectory(documents);
        File.WriteAllText(Path.Combine(documents, "waylonia.toml"), string.Empty);
        return WayloniaPaths.Sandbox(documents, Path.Combine(_root, "state"), Path.Combine(_root, "cache"));
    }

    [Fact]
    public void An_empty_config_is_a_nested_manager_run_with_nothing_to_connect()
    {
        var paths = Paths();

        var run = MobileRun.Build(paths, Mobile, BasinLogger.None);

        Assert.True(run.Manager);
        Assert.Equal(ShellMode.Nested, run.Host.Shell);
        Assert.False(run.Host.Tray);
        Assert.Null(run.LocalCommand);
        Assert.Null(run.LocalDesktop);
        Assert.Null(run.WaypipeListen);
        Assert.Empty(run.Initial);
        Assert.Same(Mobile, run.Capabilities);
        Assert.Same(paths, run.Paths);
        Assert.True(Directory.Exists(paths.SshDirectory));
        Assert.True(Directory.Exists(paths.SessionsDirectory));
    }

    [Fact]
    public void The_autoconnect_sessions_are_the_initial_ones()
    {
        var paths = Paths();
        Directory.CreateDirectory(paths.SessionsDirectory);
        File.WriteAllText(Path.Combine(paths.SessionsDirectory, "lab.toml"), "ssh = \"user@lab\"\nautoconnect = true\n");
        File.WriteAllText(Path.Combine(paths.SessionsDirectory, "idle.toml"), "ssh = \"user@idle\"\n");

        var run = MobileRun.Build(paths, Mobile, BasinLogger.None);

        Assert.Equal(["lab"], run.Initial.Select(static settings => settings.Name));
        Assert.Equal(2, run.Store.Load(BasinLogger.None).Profiles.Count);
    }

    [Fact]
    public void ExportXdg_points_the_four_variables_at_the_roots()
    {
        string[] names = ["XDG_STATE_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_CONFIG_HOME"];
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            MobileRun.ExportXdg("/tmp/state", "/tmp/cache", "/tmp/config");

            Assert.Equal("/tmp/state", Environment.GetEnvironmentVariable("XDG_STATE_HOME"));
            Assert.Equal("/tmp/state", Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
            Assert.Equal("/tmp/cache", Environment.GetEnvironmentVariable("XDG_CACHE_HOME"));
            Assert.Equal("/tmp/config", Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        }
        finally
        {
            for (var i = 0; i < names.Length; i++)
            {
                Environment.SetEnvironmentVariable(names[i], saved[i]);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
