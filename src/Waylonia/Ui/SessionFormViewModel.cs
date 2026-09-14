using Basin.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Waylonia.Cli;
using Waylonia.Sessions;

namespace Waylonia.Ui;

internal sealed partial class SessionFormViewModel : ObservableObject
{
    public const string NameField = "name";

    public const string SshField = "ssh";

    public const string CommandField = "command";

    public const string VideoField = "video";

    public const string LangField = "lang";

    public const string DesktopSizeField = "desktop-size";

    public const string HotkeysField = "hotkeys";

    public static IReadOnlyList<string> CompressChoices { get; } = ["default", "lz4", "zstd", "none"];

    public static IReadOnlyList<string> DesktopChoices { get; } =
        ["none", .. DesktopRecipes.All.Select(static recipe => recipe.Name), "custom"];

    private readonly Dictionary<string, string> _problems = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _ssh = string.Empty;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _autostart = string.Empty;

    [ObservableProperty]
    private int _compressIndex;

    [ObservableProperty]
    private bool? _gpu;

    [ObservableProperty]
    private string _video = string.Empty;

    [ObservableProperty]
    private bool? _audio;

    [ObservableProperty]
    private string _terminal = string.Empty;

    [ObservableProperty]
    private string _currentDesktop = string.Empty;

    [ObservableProperty]
    private string _lang = string.Empty;

    [ObservableProperty]
    private bool _autoconnect;

    [ObservableProperty]
    private int _desktopIndex;

    [ObservableProperty]
    private string _desktopSize = string.Empty;

    [ObservableProperty]
    private string _desktopEnv = string.Empty;

    [ObservableProperty]
    private string _hotkeys = string.Empty;

    public string? NameProblem => ProblemFor(NameField);

    public string? SshProblem => ProblemFor(SshField);

    public string? CommandProblem => ProblemFor(CommandField);

    public string? VideoProblem => ProblemFor(VideoField);

    public string? LangProblem => ProblemFor(LangField);

    public string? DesktopSizeProblem => ProblemFor(DesktopSizeField);

    public string? HotkeysProblem => ProblemFor(HotkeysField);

    public IReadOnlyDictionary<string, string> Problems => _problems;

    public string? ProblemFor(string field) => _problems.GetValueOrDefault(field);

    public void Load(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = profile.Name;
        Ssh = profile.Ssh;
        Command = profile.Command ?? string.Empty;
        Autostart = profile.Autostart is { } autostart ? string.Join('\n', autostart) : string.Empty;
        CompressIndex = Math.Max(0, IndexOf(CompressChoices, profile.Compress ?? "default"));
        Gpu = profile.Gpu;
        Video = profile.Video ?? string.Empty;
        Audio = profile.Audio;
        Terminal = profile.Terminal ?? string.Empty;
        CurrentDesktop = profile.CurrentDesktop ?? string.Empty;
        Lang = profile.Lang is "" ? "\"\"" : profile.Lang ?? string.Empty;
        Autoconnect = profile.Autoconnect;
        DesktopIndex = Math.Max(0, IndexOf(DesktopChoices, profile.Desktop ?? "none"));
        DesktopSize = profile.DesktopSize ?? string.Empty;
        DesktopEnv = profile.DesktopEnv is { } env ? string.Join('\n', env) : string.Empty;
        Hotkeys = profile.Hotkeys is { } hotkeys
            ? string.Join('\n', hotkeys.Select(static hotkey => $"{hotkey.Chord} = {hotkey.Command}"))
            : string.Empty;
        ClearProblems();
    }

    public void Clear() => Load(new SessionProfile(string.Empty, string.Empty));

    public void ClearProblems()
    {
        if (_problems.Count == 0)
        {
            return;
        }

        _problems.Clear();
        RaiseProblems();
    }

    public void Complain(string field, string message)
    {
        if (_problems.TryAdd(field, message))
        {
            RaiseProblems();
        }
    }

    public SessionProfile? Validate()
    {
        ClearProblems();
        var name = Name.Trim();
        if (name.Length == 0)
        {
            Complain(NameField, "Give the session a name.");
        }
        else if (!SessionStore.IsValidName(name))
        {
            Complain(NameField, "Use only letters, digits, '.', '_' and '-' in the name.");
        }

        var ssh = Blank(Ssh);
        if (ssh is null)
        {
            Complain(SshField, "Give the ssh destination, such as user@host.");
        }

        var video = Blank(Video);
        if (video is not null && !VideoChoice.IsValid(video))
        {
            Complain(VideoField, "Use none, h264, vp9 or av1. Add ,hw to decode on this host's GPU.");
        }

        var langText = Lang.Trim();
        var lang = langText.Length == 0 ? null : langText == "\"\"" ? string.Empty : langText;
        if (lang is { Length: > 0 } && !Config.IsLocaleName(lang))
        {
            Complain(LangField, "Use a locale name, such as en_US.UTF-8. Write \"\" to leave the remote alone.");
        }

        var desktop = DesktopIndex > 0 && DesktopIndex < DesktopChoices.Count ? DesktopChoices[DesktopIndex] : null;
        var command = Blank(Command);
        if (desktop == "custom" && command is null)
        {
            Complain(CommandField, "A custom desktop needs the command that starts it.");
        }

        var size = Blank(DesktopSize);
        if (size is not null && Program.ParseSize(size) is null)
        {
            Complain(DesktopSizeField, "Use WIDTHxHEIGHT, such as 1920x1080.");
        }

        var hotkeys = new List<Hotkey>();
        foreach (var line in Lines(Hotkeys))
        {
            var split = line.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                Complain(HotkeysField, $"Write one hotkey per line as CHORD = COMMAND. '{line}' has no '='.");
                break;
            }

            var chord = line[..split].Trim().Trim('"');
            var hotkeyCommand = line[(split + 1)..].Trim().Trim('"');
            if (hotkeyCommand.Length == 0)
            {
                Complain(HotkeysField, $"'{chord}' needs a command after the '='.");
                break;
            }

            if (Hotkey.Parse(chord, hotkeyCommand, BasinLogger.None, name) is not { } hotkey)
            {
                Complain(HotkeysField, $"'{chord}' is not a chord. Use modifiers and one key, such as ctrl+alt+t.");
                break;
            }

            hotkeys.Add(hotkey);
        }

        if (_problems.Count > 0)
        {
            return null;
        }

        var autostart = Lines(Autostart);
        var env = Lines(DesktopEnv);
        return new SessionProfile(
            name,
            ssh!,
            command,
            autostart.Count > 0 ? autostart : null,
            CompressIndex > 0 && CompressIndex < CompressChoices.Count ? CompressChoices[CompressIndex] : null,
            Gpu,
            video,
            Audio,
            Blank(Terminal),
            Blank(CurrentDesktop),
            lang,
            Autoconnect,
            desktop,
            size,
            env.Count > 0 ? env : null,
            hotkeys.Count > 0 ? hotkeys : null);
    }

    private void RaiseProblems()
    {
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(SshProblem));
        OnPropertyChanged(nameof(CommandProblem));
        OnPropertyChanged(nameof(VideoProblem));
        OnPropertyChanged(nameof(LangProblem));
        OnPropertyChanged(nameof(DesktopSizeProblem));
        OnPropertyChanged(nameof(HotkeysProblem));
    }

    private static int IndexOf(IReadOnlyList<string> choices, string value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static string? Blank(string? text) => text is { } value && value.Trim().Length > 0 ? value.Trim() : null;

    private static List<string> Lines(string? text) =>
        (text ?? string.Empty).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
