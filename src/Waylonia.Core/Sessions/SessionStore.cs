using System.Text;
using Basin.Diagnostics;
using Tomlyn.Model;
using Waylonia.Cli;

namespace Waylonia.Sessions;

internal sealed class SessionStore(string? directory)
{
    public const string NamePattern = "[A-Za-z0-9._-]+";

    public string? Directory { get; } = directory;

    public event Action? Changed;

    public static string? DirectoryFor(string? configPath) =>
        configPath is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "sessions");

    public static bool IsValidName(string? name) =>
        name is { Length: > 0 and <= 64 }
        && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
        && name is not ("." or "..");

    public static string? WhyInvalidName(string? name) => IsValidName(name)
        ? null
        : name is not { Length: > 0 }
            ? "Give the session a name made of letters, digits, '.', '_' and '-'."
            : $"'{name}' isn't a session name. Use up to 64 letters, digits, '.', '_' and '-'.";

    public static string UserKeyProblem(DestinationKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return $"The user setting only applies to an Incus instance. {kind.UserNote}";
    }

    public string? PathOf(string name) => Directory is null ? null : Path.Combine(Directory, name + ".toml");

    public SessionCatalog Load(BasinLogger log)
    {
        if (Directory is null || !System.IO.Directory.Exists(Directory))
        {
            return SessionCatalog.Empty;
        }

        var profiles = new List<SessionProfile>();
        var broken = new List<BrokenSession>();
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory, "*.toml");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn(error, $"sessions: listing {Directory} failed, no saved sessions loaded");
            return SessionCatalog.Empty;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!IsValidName(name))
            {
                broken.Add(new BrokenSession(name, file, WhyInvalidName(name)!));
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Debug(error, $"sessions: reading {file} failed");
                broken.Add(new BrokenSession(name, file, $"Couldn't read the file. {UiSentence.Of(error.Message)}"));
                continue;
            }

            if (Parse(name, text, log, out var error2) is { } profile)
            {
                profiles.Add(profile);
            }
            else
            {
                broken.Add(new BrokenSession(name, file, error2 ?? "Couldn't read the file."));
            }
        }

        foreach (var entry in broken)
        {
            log.Warn($"session {entry.Name}: {entry.Path} not loaded, {CliMessage.Text(entry.Error)}");
        }

        return new SessionCatalog(profiles, broken);
    }

    public static SessionProfile? Parse(string name, string text, BasinLogger log, out string? error)
    {
        TomlTable table;
        try
        {
            table = Tomlyn.Toml.ToModel(text);
        }
        catch (Tomlyn.TomlException failure)
        {
            error = failure.Message;
            return null;
        }

        return FromTable(name, table, log, out error);
    }

    public static SessionProfile? FromTable(string name, TomlTable table, BasinLogger log, out string? error)
    {
        var named = new List<(DestinationKind Kind, string Name)>();
        foreach (var kind in DestinationKinds.All)
        {
            var text = kind.IsSsh
                ? Config.Text(table, kind.Key)
                : table.TryGetValue(kind.Key, out var value) && value is string raw ? raw.Trim() : null;
            if (text is not null)
            {
                named.Add((kind, text));
            }
        }

        if (named.Count > 1)
        {
            error = DestinationKinds.BothProblem(named[0].Kind, named[1].Kind);
            return null;
        }

        if (named.Count == 0)
        {
            error = "This session doesn't say where to connect. Add an ssh, wsl, distrobox, toolbox or incus line.";
            return null;
        }

        var (destinationKind, targetName) = named[0];
        var ssh = targetName;
        if (!destinationKind.IsSsh)
        {
            if (destinationKind.NameProblem(targetName) is { } nameProblem)
            {
                error = nameProblem;
                return null;
            }

            ssh = destinationKind.For(targetName);
        }

        var user = Config.Text(table, "user");
        if (user is not null)
        {
            if (!destinationKind.TakesUser)
            {
                error = UserKeyProblem(destinationKind);
                return null;
            }

            if (UserProblem(user) is { } userProblem)
            {
                error = userProblem;
                return null;
            }
        }

        foreach (var (key, _) in table)
        {
            if (key is not ("ssh" or "wsl" or "distrobox" or "toolbox" or "incus" or "user" or "command" or "autostart" or "compress" or "gpu" or "video" or "audio"
                or "terminal" or "current-desktop" or "lang" or "autoconnect" or "desktop" or "desktop-size"
                or "desktop-env" or "hotkeys"))
            {
                log.Warn($"session {name}: unknown key '{key}' ignored");
            }
        }

        var video = Config.Text(table, "video");
        if (video is not null && !VideoChoice.IsValid(video))
        {
            error = VideoChoice.Problem(video);
            return null;
        }

        var compress = Config.Text(table, "compress");
        if (compress is not null && compress is not ("lz4" or "zstd" or "none"))
        {
            error = $"'{compress}' isn't a compression setting. Use lz4, zstd or none.";
            return null;
        }

        var lang = Config.LocaleName(table, log);
        var desktop = Config.Text(table, "desktop");
        if (desktop is not null && desktop != "custom" && DesktopRecipes.Find(desktop) is null)
        {
            error = DesktopRecipes.UnknownProblem(desktop);
            return null;
        }

        IReadOnlyList<Hotkey>? hotkeys = null;
        if (table.TryGetValue("hotkeys", out var hotkeyValue) && hotkeyValue is TomlTable hotkeyTable)
        {
            var parsed = new List<Hotkey>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (Hotkey.Parse(chord, Config.CommandText(value), log, name) is { } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            hotkeys = parsed;
        }

        error = null;
        return new SessionProfile(
            name,
            ssh,
            Config.CommandText(table, "command"),
            table.ContainsKey("autostart") ? Config.Commands(table, "autostart") : null,
            compress,
            Config.Flag(table, "gpu"),
            video,
            Config.Flag(table, "audio"),
            Config.CommandText(table, "terminal"),
            Config.Text(table, "current-desktop"),
            lang,
            Config.Flag(table, "autoconnect") ?? false,
            desktop,
            Config.Text(table, "desktop-size"),
            table.ContainsKey("desktop-env") ? Config.Assignments(table, "desktop-env") : null,
            hotkeys,
            user);
    }

    public static string? UserProblem(string user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Length == 0 || user[0] == '-' || !user.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? $"'{user}' isn't an account name. Use only letters, digits, '.', '_' and '-', and don't start with '-'."
            : null;
    }

    public static string Render(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var text = new StringBuilder();
        var kind = DestinationKinds.For(profile.Ssh);
        Line(text, kind.Key, kind.TryParse(profile.Ssh, out var target) ? target : profile.Ssh);
        Line(text, "user", profile.User);

        Line(text, "command", profile.Command);
        Line(text, "autostart", profile.Autostart);
        Line(text, "compress", profile.Compress);
        Line(text, "gpu", profile.Gpu);
        Line(text, "video", profile.Video);
        Line(text, "audio", profile.Audio);
        Line(text, "terminal", profile.Terminal);
        Line(text, "current-desktop", profile.CurrentDesktop);
        Line(text, "lang", profile.Lang);
        if (profile.Autoconnect)
        {
            Line(text, "autoconnect", true);
        }

        Line(text, "desktop", profile.Desktop);
        Line(text, "desktop-size", profile.DesktopSize);
        Line(text, "desktop-env", profile.DesktopEnv);
        if (profile.Hotkeys is { Count: > 0 } hotkeys)
        {
            text.Append("\n[hotkeys]\n");
            foreach (var hotkey in hotkeys)
            {
                text.Append(Quote(hotkey.Chord)).Append(" = ").Append(Quote(hotkey.Command)).Append('\n');
            }
        }

        return text.ToString();
    }

    public void Save(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (WhyInvalidName(profile.Name) is { } why)
        {
            throw new ArgumentException(why, nameof(profile));
        }

        if (PathOf(profile.Name) is not { } path)
        {
            throw new InvalidOperationException("Sessions can't be saved, because the config file is off. Start Waylonia without --config false to save them.");
        }

        System.IO.Directory.CreateDirectory(Directory!);
        File.WriteAllText(path, Render(profile));
        Changed?.Invoke();
    }

    public bool Delete(string name)
    {
        if (PathOf(name) is not { } path || !File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        Changed?.Invoke();
        return true;
    }

    private static void Line(StringBuilder text, string key, string? value)
    {
        if (value is not null)
        {
            text.Append(key).Append(" = ").Append(Quote(value)).Append('\n');
        }
    }

    private static void Line(StringBuilder text, string key, bool? value)
    {
        if (value is { } flag)
        {
            text.Append(key).Append(" = ").Append(flag ? "true" : "false").Append('\n');
        }
    }

    private static void Line(StringBuilder text, string key, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        text.Append(key).Append(" = [").Append(string.Join(", ", values.Select(Quote))).Append("]\n");
    }

    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case < ' ':
                    text.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
