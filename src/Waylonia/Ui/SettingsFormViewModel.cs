using CommunityToolkit.Mvvm.ComponentModel;
using Waylonia.Cli;

namespace Waylonia.Ui;

internal sealed partial class SettingsFormViewModel : FormViewModel
{
    public const string CaptureChordField = "capture-chord";

    public const string VideoField = "video";

    public const string LangField = "lang";

    public const string SocketField = "socket";

    public const string HotkeysField = "hotkeys";

    public static IReadOnlyList<string> CompressChoices { get; } = ["lz4 (default)", "lz4", "zstd", "none"];

    [ObservableProperty]
    private bool _xWayland = true;

    [ObservableProperty]
    private bool _tray = true;

    [ObservableProperty]
    private bool _trayApps = true;

    [ObservableProperty]
    private bool _clipboard = true;

    [ObservableProperty]
    private bool _drag = true;

    [ObservableProperty]
    private bool _followCursor = true;

    [ObservableProperty]
    private bool _gtkDpi = true;

    [ObservableProperty]
    private bool _sessionTitles = true;

    [ObservableProperty]
    private string _captureChord = string.Empty;

    [ObservableProperty]
    private int _compressIndex;

    [ObservableProperty]
    private bool? _gpu;

    [ObservableProperty]
    private bool? _audio;

    [ObservableProperty]
    private string _video = string.Empty;

    [ObservableProperty]
    private string _lang = string.Empty;

    [ObservableProperty]
    private string _terminal = string.Empty;

    [ObservableProperty]
    private string _currentDesktop = string.Empty;

    [ObservableProperty]
    private string _socket = string.Empty;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _hotkeys = string.Empty;

    public string? CaptureChordProblem => ProblemFor(CaptureChordField);

    public string? VideoProblem => ProblemFor(VideoField);

    public string? LangProblem => ProblemFor(LangField);

    public string? SocketProblem => ProblemFor(SocketField);

    public string? HotkeysProblem => ProblemFor(HotkeysField);

    protected override IReadOnlyList<string> ProblemProperties { get; } =
    [
        nameof(CaptureChordProblem), nameof(VideoProblem), nameof(LangProblem), nameof(SocketProblem), nameof(HotkeysProblem),
    ];

    public void Load(ConfigValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        XWayland = values.XWayland;
        Tray = values.Tray;
        TrayApps = values.TrayApps;
        Clipboard = values.Clipboard;
        Drag = values.Drag;
        FollowCursor = values.FollowCursor;
        GtkDpi = values.GtkDpi;
        SessionTitles = values.SessionTitles;
        CaptureChord = values.CaptureChord == ConfigValues.DefaultCaptureChord ? string.Empty : values.CaptureChord;
        CompressIndex = Math.Max(0, values.Compress is null ? 0 : IndexOf(CompressChoices, values.Compress));
        Gpu = values.Gpu;
        Audio = values.Audio;
        Video = values.Video ?? string.Empty;
        Lang = values.Lang switch
        {
            ConfigValues.DefaultLang => string.Empty,
            "" => "\"\"",
            var lang => lang,
        };
        Terminal = values.Terminal ?? string.Empty;
        CurrentDesktop = values.CurrentDesktop ?? string.Empty;
        Socket = values.Socket ?? string.Empty;
        Command = values.Command ?? string.Empty;
        Hotkeys = HotkeyLines.Render(values.Hotkeys);
        ClearProblems();
    }

    public ConfigValues? Validate()
    {
        ClearProblems();
        var chord = Blank(CaptureChord) ?? ConfigValues.DefaultCaptureChord;
        if (Waylonia.CaptureChord.Parse(chord, Basin.Diagnostics.BasinLogger.None) is null)
        {
            Complain(CaptureChordField, "Use one modifier key, such as RightControl, or double:RightControl for a double tap.");
        }

        var video = Blank(Video);
        if (video is not null && !VideoChoice.IsValid(video))
        {
            Complain(VideoField, "Use none, h264, vp9 or av1. Add ,hw to decode on this host's GPU.");
        }

        var langText = Lang.Trim();
        var lang = langText.Length == 0 ? ConfigValues.DefaultLang : langText == "\"\"" ? string.Empty : langText;
        if (lang.Length > 0 && !Config.IsLocaleName(lang))
        {
            Complain(LangField, "Use a locale name, such as en_US.UTF-8. Write \"\" to leave the remote alone.");
        }

        var socket = Blank(Socket);
        if (socket is not null && socket.Any(static c => char.IsWhiteSpace(c) || c is '/' or '\\'))
        {
            Complain(SocketField, "Use a socket name, such as wayland-9, not a path.");
        }

        var hotkeys = HotkeyLines.Parse(Hotkeys, null, out var hotkeysProblem);
        if (hotkeysProblem is not null)
        {
            Complain(HotkeysField, hotkeysProblem);
        }

        if (HasProblems)
        {
            return null;
        }

        return new ConfigValues(
            CompressIndex > 0 && CompressIndex < CompressChoices.Count ? CompressChoices[CompressIndex] : null,
            Gpu,
            Audio,
            video,
            socket,
            Blank(Command),
            Blank(Terminal),
            Blank(CurrentDesktop),
            lang,
            XWayland,
            Tray,
            TrayApps,
            Clipboard,
            Drag,
            FollowCursor,
            GtkDpi,
            SessionTitles,
            chord,
            hotkeys);
    }
}
