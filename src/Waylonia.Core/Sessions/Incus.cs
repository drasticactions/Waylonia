using System.Globalization;
using System.Text.Json;

namespace Waylonia.Sessions;

internal static class Incus
{
    public const string Program = "incus";

    public const string DevicePrefix = "waylonia-";

    public const string Wrapper =
        $"echo \"{PidMarker.Prefix}$$\" >&2; " +
        "if [ \"$(cut -d ' ' -f 5 /proc/$$/stat 2>/dev/null)\" != \"$$\" ] && command -v setsid >/dev/null 2>&1; " +
        "then exec setsid sh -c \"$1\"; fi; exec sh -c \"$1\"";

    public static IReadOnlyList<string> List() => ["list", "-f", "json"];

    public static IReadOnlyList<string> Start(string name) => ["start", name];

    public static IReadOnlyList<string> Exec(string name, IncusUser user, params IReadOnlyList<string> command)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(command);
        return
        [
            "exec", "-T", name,
            "--user", Number(user.Uid), "--group", Number(user.Gid), "--cwd", user.Home,
            "--env", $"HOME={user.Home}", "--env", $"USER={user.Name}", "--env", $"LOGNAME={user.Name}",
            "--", .. command,
        ];
    }

    public static IReadOnlyList<string> Script(string name, IncusUser user, string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Exec(name, user, "sh", "-c", Wrapper, "sh", script);
    }

    public static IReadOnlyList<string> Probe(string name) => ["exec", "-T", name, "--", "true"];

    public static IReadOnlyList<string> Passwd(string name, string key) => ["exec", "-T", name, "--", "getent", "passwd", key];

    public static IReadOnlyList<string> Signal(string name, string signal, int pid) =>
        ["exec", "-T", name, "--", "sh", "-c", $"kill -s {signal} -- -{Number(pid)} 2>/dev/null || kill -s {signal} {Number(pid)}"];

    public static IReadOnlyList<string> AddDisk(string name, string device, string source, string path) =>
        ["config", "device", "add", name, device, "disk", $"source={source}", $"path={path}"];

    public static IReadOnlyList<string> AddX11Proxy(string name, string device, int display, int uid, int gid)
    {
        var socket = $"unix:/tmp/.X11-unix/X{Number(display)}";
        return
        [
            "config", "device", "add", name, device, "proxy", "bind=container",
            $"listen={socket}", $"connect={socket}",
            $"security.uid={Number(uid)}", $"security.gid={Number(gid)}", "mode=0777",
        ];
    }

    public static IReadOnlyList<string> RemoveDevice(string name, string device) => ["config", "device", "remove", name, device];

    public static string X11Device(string device) => device + "-x11";

    public static string MountPath(string device) => $"/run/{device}";

    public static string ClientDisplay(string device) => $"{MountPath(device)}/wayland-0";

    public static int? DisplayNumber(string? display)
    {
        if (display is not { Length: > 1 } || display[0] != ':')
        {
            return null;
        }

        var digits = display.AsSpan(1);
        var dot = digits.IndexOf('.');
        if (dot >= 0)
        {
            digits = digits[..dot];
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public static IReadOnlyList<IncusInstance> ParseInstances(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var instances = new List<IncusInstance>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var config = element.TryGetProperty("config", out var c) ? c : default;
            uint? vsock = config.ValueKind == JsonValueKind.Object
                && config.TryGetProperty("volatile.vsock_id", out var v)
                && uint.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
            var devices = element.TryGetProperty("devices", out var d) && d.ValueKind == JsonValueKind.Object
                ? d.EnumerateObject().Select(static device => device.Name).ToList()
                : [];
            var hasGpu = element.TryGetProperty("expanded_devices", out var expanded) && expanded.ValueKind == JsonValueKind.Object
                && expanded.EnumerateObject().Any(static device =>
                    device.Value.TryGetProperty("type", out var type) && type.GetString() == "gpu");
            instances.Add(new IncusInstance(
                element.GetProperty("name").GetString() ?? string.Empty,
                element.TryGetProperty("type", out var kind) && kind.GetString() == "virtual-machine",
                element.TryGetProperty("status", out var status) && status.GetString() == "Running",
                element.TryGetProperty("project", out var project) ? project.GetString() ?? "default" : "default",
                vsock,
                devices,
                hasGpu));
        }

        return instances;
    }

    public static IncusUser? ParsePasswd(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var fields = line.Trim().Split(':');
        return fields.Length >= 6
            && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
            && int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var gid)
            ? new IncusUser(fields[0], uid, gid, fields[5].Length > 0 ? fields[5] : "/")
            : null;
    }

    public static int? DevicePid(string device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.StartsWith(DevicePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = device.AsSpan(DevicePrefix.Length);
        var dash = rest.IndexOf('-');
        return dash > 0 && int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
    }

    public static IReadOnlyList<string> StaleDevices(IEnumerable<string> devices, Func<int, bool> isLiveWaylonia)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(isLiveWaylonia);
        return [.. devices.Where(device => DevicePid(device) is { } pid && !isLiveWaylonia(pid))];
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
