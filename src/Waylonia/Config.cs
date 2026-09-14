using Waylonia.Cli;
using Waylonia.Sessions;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace Waylonia;

internal sealed class Config
{
    public string? Compress { get; private set; }

    public bool? Gpu { get; private set; }

    public bool? Audio { get; private set; }

    public string? Video { get; private set; }

    public string? Socket { get; private set; }

    public string? Command { get; private set; }

    public bool XWayland { get; private set; } = true;

    public bool Tray { get; private set; } = true;

    public bool TrayApps { get; private set; } = true;

    public bool Clipboard { get; private set; } = true;

    public bool Drag { get; private set; } = true;

    public bool FollowCursor { get; private set; } = true;

    public bool GtkDpi { get; private set; } = true;

    public bool SessionTitles { get; private set; } = true;

    public string CaptureChord { get; private set; } = "double:RightControl";

    public string? Terminal { get; private set; }

    public string? CurrentDesktop { get; private set; }

    public string Lang { get; private set; } = "C.UTF-8";

    public string? Path { get; private set; }

    public string? SessionsDirectory { get; private set; }

    public IReadOnlyDictionary<string, DesktopProfile> Desktops { get; private set; } =
        new Dictionary<string, DesktopProfile>();

    public IReadOnlyList<SessionProfile> LegacyHosts { get; private set; } = [];

    public IReadOnlyList<Hotkey> Hotkeys { get; private set; } = [];

    public HostSettings Host => new(
        XWayland, Tray, TrayApps, Clipboard, Drag, FollowCursor, GtkDpi, CaptureChord, SessionTitles,
        Hotkeys, Terminal, CurrentDesktop);

    public static Config Load(bool skipFile, string? path, BasinLogger log)
    {
        var config = new Config();
        if (skipFile)
        {
            return config;
        }

        var explicitPath = path is not null;
        path ??= TomlConfig.DefaultPath("waylonia");
        config.Path = path;
        config.SessionsDirectory = SessionStore.DirectoryFor(path);
        if (!explicitPath && !File.Exists(path))
        {
            WritePlaceholder(path, log);
            return config;
        }

        if (TomlConfig.Read(path, log) is not { } table)
        {
            return config;
        }

        foreach (var (name, value) in table)
        {
            if (value is TomlTable && name is not ("host" or "hosts" or "hotkeys" or "desktops"))
            {
                log.Warn($"{path} has an unknown section '[{name}]', ignoring it; a remote session is a file in {config.SessionsDirectory}");
            }
        }

        config.Compress = Compression(table, "compress", log);
        config.Gpu = Flag(table, "gpu");
        config.Audio = Flag(table, "audio");
        if (table.TryGetValue("video", out var video) && video is string videoCodec)
        {
            if (VideoChoice.IsValid(videoCodec))
            {
                config.Video = videoCodec;
            }
            else
            {
                log.Warn($"Invalid --video, ignoring '{videoCodec}'");
            }
        }

        if (table.TryGetValue("socket", out var socket) && socket is string socketName && socketName.Length > 0)
        {
            config.Socket = socketName;
        }

        config.Command = CommandText(table, "command");
        config.Terminal = CommandText(table, "terminal");
        config.CurrentDesktop = Text(table, "current-desktop");
        config.Lang = LocaleName(table, log) ?? config.Lang;

        if (table.TryGetValue("host", out var host) && host is TomlTable hostTable)
        {
            config.XWayland = Toggle(hostTable, "xwayland", config.XWayland);
            config.Tray = Toggle(hostTable, "tray", config.Tray);
            config.TrayApps = Toggle(hostTable, "tray-apps", config.TrayApps);
            config.Clipboard = Toggle(hostTable, "clipboard", config.Clipboard);
            config.Drag = Toggle(hostTable, "drag", config.Drag);
            config.FollowCursor = Toggle(hostTable, "follow-cursor", config.FollowCursor);
            config.GtkDpi = Toggle(hostTable, "gtk-dpi", config.GtkDpi);
            config.SessionTitles = Toggle(hostTable, "session-titles", config.SessionTitles);
            if (hostTable.TryGetValue("capture-chord", out var chord)
                && chord is string chordText
                && chordText.Trim().Length > 0)
            {
                config.CaptureChord = chordText.Trim();
            }
        }

        if (table.TryGetValue("hosts", out var hosts) && hosts is TomlTable hostsTable)
        {
            var parsed = new List<SessionProfile>();
            foreach (var (name, value) in hostsTable)
            {
                if (value is not TomlTable profileTable)
                {
                    continue;
                }

                log.Warn($"[hosts.{name}] moved to sessions/{name}.toml; run waylonia --migrate-hosts to convert it");
                if (SessionStore.FromTable(name, profileTable, BasinLogger.None, out var why) is { } profile
                    && SessionStore.IsValidName(name))
                {
                    parsed.Add(profile);
                }
                else
                {
                    log.Warn($"[hosts.{name}] cannot become a session: {why ?? SessionStore.WhyInvalidName(name)}");
                }
            }

            config.LegacyHosts = parsed;
        }

        if (table.TryGetValue("desktops", out var desktops) && desktops is TomlTable desktopsTable)
        {
            var parsed = new Dictionary<string, DesktopProfile>();
            foreach (var (name, value) in desktopsTable)
            {
                if (value is not TomlTable profileTable)
                {
                    continue;
                }

                parsed[name] = new DesktopProfile(
                    name,
                    Text(profileTable, "recipe"),
                    Text(profileTable, "host"),
                    Text(profileTable, "size"),
                    CommandText(profileTable, "command"),
                    Assignments(profileTable, "env"),
                    Flag(profileTable, "gpu"),
                    Text(profileTable, "video"));
            }

            config.Desktops = parsed;
        }

        if (table.TryGetValue("hotkeys", out var hotkeys) && hotkeys is TomlTable hotkeyTable)
        {
            var parsed = new List<Hotkey>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (Hotkey.Parse(chord, CommandText(value), log) is { } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            config.Hotkeys = parsed;
        }

        return config;
    }

    internal static void WritePlaceholder(string path, BasinLogger log)
    {
        const string placeholder = """
            # The waypipe channel compression: "lz4", "zstd" or "none". A session
            # file may override every setting up to [host].
            #compress = "lz4"

            # Advertise dmabuf to the remote session. Each remote buffer is
            # backed by a host memory region. Off keeps the session shm-only.
            #gpu = true

            # Ask the remote waypipe to encode buffer updates as video, and
            # decode them here with the system FFmpeg. Implies gpu. Append
            # ",hw" to decode on this host's GPU when it has a device, and
            # ",hwenc", ",swenc", ",hwdec", ",swdec" or ",bpf=B" to say where
            # the remote encodes and decodes, and at how many bits per frame.
            #video = "h264,hw,hwenc,bpf=7.5e5"

            # Play the remote session's sound on this host. It is captured
            # from a sink of its own on the remote and streamed over the same
            # ssh connection, which costs about 384 kB/s. Off by default,
            # because a local client already plays to this host's sound server.
            # Every session with sound is mixed into the one playback device.
            #audio = true

            # The Wayland socket name to bind, where the platform has one.
            #socket = "wayland-9"

            # The local client a bare `waylonia` spawns on Linux; a string or an
            # argv array. Without it a bare `waylonia` sits in the tray, where
            # "Sessions…" opens the session manager.
            #command = "foot"

            # The terminal the tray menu wraps around an application that says
            # Terminal=true, such as htop. Used as written, so it must end in
            # whatever takes a command: "-e" for the terminals that follow xterm,
            # nothing for xdg-terminal-exec. Unset leaves those entries out.
            #terminal = "foot -e"

            # What XDG_CURRENT_DESKTOP would say, for entries that list
            # OnlyShowIn or NotShowIn. Unset lists no OnlyShowIn entry.
            #current-desktop = "GNOME"

            # The LANG an ssh session gets when the remote login sets none.
            # A non-interactive ssh shell usually has no locale at all, which
            # makes terminal programs like btop refuse to start. "" leaves the
            # remote alone.
            #lang = "C.UTF-8"

            # Host desktop integration; every toggle defaults to on.
            #[host]
            #xwayland = true
            #tray = true
            # List each session's applications in the tray menu, by category,
            # and launch one from there. Needs tray.
            #tray-apps = true
            #clipboard = true
            #drag = true
            # Open each new client window on the screen the pointer is on, rather
            # than wherever the host desktop would put it.
            #follow-cursor = true
            # Read a session's GTK settings through a staged copy whose
            # gtk-xft-dpi is 96, so a remote desktop's own display scaling does
            # not size GTK windows twice. Off leaves the remote config alone.
            #gtk-dpi = true
            # End every window title with " — NAME", the session it belongs to.
            #session-titles = true

            # Take the host's own keyboard and pointer for a nested desktop.
            # A double tap of one modifier within 400 ms toggles it.
            #capture-chord = "double:RightControl"

            # Remote sessions live one per file in the sessions/ directory
            # beside this file, as sessions/NAME.toml, and the session manager
            # window writes them. `waylonia --ssh NAME` connects one; a session
            # with autoconnect = true connects when waylonia starts. One looks
            # like this:
            #
            #   ssh = "user@devbox"
            #   command = "tmux new -A -s main"
            #   autostart = ["foot", "firefox"]
            #   compress = "none"
            #   gpu = true
            #   video = "h264,hw"
            #   audio = true
            #   terminal = "xdg-terminal-exec"
            #   current-desktop = "KDE"
            #   lang = "en_US.UTF-8"
            #   autoconnect = true
            #   desktop = "plasma"
            #   desktop-size = "1920x1080"
            #   desktop-env = ["QT_QPA_PLATFORM=wayland"]
            #
            #   [hotkeys]
            #   "ctrl+alt+t" = "foot"

            # Whole-desktop sessions. --desktop NAME matches a session file
            # first, then one of these, then a built-in recipe name: sway,
            # niri, plasma, cosmic or xfce. host names a session.
            #[desktops.plasma]
            #recipe = "plasma"
            #host = "lab"
            #size = "1920x1080"
            #command = "startplasma-wayland"
            #env = ["QT_QPA_PLATFORM=wayland"]
            #gpu = false
            #video = "none"

            # Host-global hotkeys. Each runs on this machine, or on the session
            # `waylonia --ssh` opened.
            #[hotkeys]
            #"ctrl+alt+t" = "foot"

            """;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(placeholder);
            log.Info($"wrote a placeholder config to {path}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot write a placeholder config to {path}: {error.Message}");
        }
    }

    private static bool Toggle(TomlTable table, string key, bool fallback) =>
        TomlConfig.Flag(table, key, fallback);

    internal static bool? Flag(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is bool flag ? flag : null;

    internal static string? Compression(TomlTable table, string key, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value) || value is not string name)
        {
            return null;
        }

        if (name is "lz4" or "zstd" or "none")
        {
            return name;
        }

        log.Warn($"compress takes lz4, zstd or none, ignoring '{name}'");
        return null;
    }

    internal static string? LocaleName(TomlTable table, BasinLogger log)
    {
        if (!table.TryGetValue("lang", out var value) || value is not string text)
        {
            return null;
        }

        var lang = text.Trim();
        if (IsLocaleName(lang))
        {
            return lang;
        }

        log.Warn($"lang '{lang}' is not a locale name like C.UTF-8 or en_US.UTF-8, ignoring it");
        return null;
    }

    internal static bool IsLocaleName(string lang) =>
        lang.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '@' or '-');

    internal static string? Text(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string text && text.Trim().Length > 0
            ? text.Trim()
            : null;

    internal static IReadOnlyList<string> Assignments(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string single when single.Trim().Length > 0 => [single.Trim()],
            TomlArray array => array.OfType<string>()
                .Select(static part => part.Trim())
                .Where(static part => part.Length > 0)
                .ToArray(),
            _ => [],
        };
    }

    internal static IReadOnlyList<string> Commands(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string single when single.Trim().Length > 0 => [single.Trim()],
            TomlArray array => array
                .Select(static item => item is TomlArray argv ? CommandText(argv) : item as string)
                .Select(static part => part?.Trim())
                .Where(static part => part is { Length: > 0 })
                .Select(static part => part!)
                .ToArray(),
            _ => [],
        };
    }

    internal static string? CommandText(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? CommandText(value) : null;

    internal static string? CommandText(object? value)
    {
        var text = value switch
        {
            string command => command.Trim(),
            TomlArray array => string.Join(
                ' ',
                array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)),
            _ => string.Empty,
        };
        return text.Length > 0 ? text : null;
    }
}
