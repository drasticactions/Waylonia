using Waylonia.Sessions;

namespace Waylonia;

internal sealed record WayloniaPaths(
    string ConfigFile,
    string SessionsDirectory,
    string StateFile,
    string IconCacheRoot,
    string SshDirectory,
    bool ConfigExplicit = false)
{
    public static WayloniaPaths Xdg(string appName = "waylonia")
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configFile = Path.Combine(XdgHome("XDG_CONFIG_HOME", Path.Combine(home, ".config")), appName, appName + ".toml");
        return new(
            configFile,
            SessionStore.DirectoryFor(configFile)!,
            Path.Combine(XdgHome("XDG_STATE_HOME", Path.Combine(home, ".local", "state")), appName, "shell.toml"),
            Path.Combine(XdgHome("XDG_CACHE_HOME", Path.Combine(home, ".cache")), appName, "icons"),
            Path.Combine(home, ".ssh"));
    }

    public static WayloniaPaths Sandbox(string documents, string stateRoot, string cacheRoot, string appName = "waylonia")
    {
        ArgumentException.ThrowIfNullOrEmpty(documents);
        ArgumentException.ThrowIfNullOrEmpty(stateRoot);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        ArgumentException.ThrowIfNullOrEmpty(appName);
        var configFile = Path.Combine(documents, appName + ".toml");
        return new(
            configFile,
            SessionStore.DirectoryFor(configFile)!,
            Path.Combine(stateRoot, appName, "shell.toml"),
            Path.Combine(cacheRoot, appName, "icons"),
            Path.Combine(documents, "ssh"));
    }

    public static string XdgHome(string variable, string fallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(variable);
        ArgumentException.ThrowIfNullOrEmpty(fallback);
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) || !Path.IsPathRooted(value) ? fallback : value;
    }

    public static WayloniaPaths ConfigOnly(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        return new(file, SessionStore.DirectoryFor(file)!, string.Empty, string.Empty, string.Empty, ConfigExplicit: true);
    }

    public bool HasConfig => ConfigFile.Length > 0;

    public WayloniaPaths WithConfig(string? file) => file is null
        ? this with { ConfigFile = string.Empty, SessionsDirectory = string.Empty, ConfigExplicit = true }
        : this with { ConfigFile = file, SessionsDirectory = SessionStore.DirectoryFor(file)!, ConfigExplicit = true };
}
