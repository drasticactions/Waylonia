namespace Waylonia.Agent;

internal sealed record AgentEnvironment(IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> Unset)
{
    public const string WaylandName = "wayland-0";

    public static IReadOnlyList<string> HostVariables { get; } =
    [
        "WAYLAND_DISPLAY",
        "WAYLAND_SOCKET",
        "DISPLAY",
        "DBUS_SESSION_BUS_ADDRESS",
        "SSH_AUTH_SOCK",
        "XDG_ACTIVATION_TOKEN",
        "DESKTOP_STARTUP_ID",
        "NO_AT_BRIDGE",
        "BASIN_SOCKET",
        "BASIN_IPC_PATH",
        "SWAYSOCK",
        "I3SOCK",
        "HYPRLAND_INSTANCE_SIGNATURE",
        "NIRI_SOCKET",
    ];

    public static AgentEnvironment Build(AgentProfile profile, string runtimeDirectory, string? display, string? busAddress)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrEmpty(runtimeDirectory);
        var home = profile.Home;
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
            ["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share"),
            ["XDG_STATE_HOME"] = Path.Combine(home, ".local", "state"),
            ["XDG_CACHE_HOME"] = Path.Combine(home, ".cache"),
            ["XDG_RUNTIME_DIR"] = runtimeDirectory,
            ["WAYLAND_DISPLAY"] = WaylandName,
            ["XDG_SESSION_TYPE"] = "wayland",
            ["GTK_A11Y"] = "atspi",
            ["QT_LINUX_ACCESSIBILITY_ALWAYS_ON"] = "1",
            ["ACCESSIBILITY_ENABLED"] = "1",
            ["GNOME_ACCESSIBILITY"] = "1",
            ["GTK_USE_PORTAL"] = "0",
            ["GDK_DEBUG"] = "no-portals",
        };
        if (display is { Length: > 0 })
        {
            env["DISPLAY"] = display;
        }

        if (busAddress is { Length: > 0 })
        {
            env["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        }

        var unset = HostVariables.Where(name => !env.ContainsKey(name)).ToArray();
        return new AgentEnvironment(env, unset);
    }

    public IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        var values = new Dictionary<string, string>(inherited, StringComparer.Ordinal);
        foreach (var name in Unset)
        {
            values.Remove(name);
        }

        foreach (var (name, value) in Env)
        {
            values[name] = value;
        }

        return values;
    }

    public static IReadOnlyDictionary<string, string> Inherited()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        return values;
    }
}
