using Basin.Diagnostics;
using Waylonia.Shell;

namespace Waylonia.Sessions;

internal static class MobileRun
{
    public static void ExportXdg(string stateRoot, string cacheRoot, string configRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(stateRoot);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        ArgumentException.ThrowIfNullOrEmpty(configRoot);
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", stateRoot);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", stateRoot);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cacheRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configRoot);
    }

    public static WayloniaRun Build(WayloniaPaths paths, HostCapabilities capabilities, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(capabilities);
        CreateDirectory(paths.SshDirectory, log);
        CreateDirectory(paths.SessionsDirectory, log);
        var config = Config.Load(paths, log);
        var store = new SessionStore(config.SessionsDirectory);
        var host = config.Host with { Shell = ShellMode.Nested, Tray = false };
        var initial = RunRules.Autoconnect(store.Load(log), [], config, "f32", log);
        return new WayloniaRun(
            capabilities,
            paths,
            host,
            initial,
            Manager: true,
            LocalCommand: null,
            LocalDesktop: null,
            WaypipeListen: null,
            Frames: 0,
            Screenshot: null,
            SocketName: null,
            config,
            store);
    }

    private static void CreateDirectory(string path, BasinLogger log)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot create {path}: {error.Message}");
        }
    }
}
