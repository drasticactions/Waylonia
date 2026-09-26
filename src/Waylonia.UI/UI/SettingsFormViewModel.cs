using System.Globalization;
using Basin.Frames.Metacity;
using Basin.Shell.Nested;
using CommunityToolkit.Mvvm.ComponentModel;
using Waylonia.Cli;
using Waylonia.Shell;

namespace Waylonia.UI;

internal sealed partial class SettingsFormViewModel : FormViewModel
{
    public const string CaptureChordField = "capture-chord";

    public const string SshTimeoutField = "ssh-timeout";

    public const string VideoField = "video";

    public const string LangField = "lang";

    public const string SocketField = "socket";

    public const string HotkeysField = "hotkeys";

    public const string ThemeField = "theme";

    public const string ButtonLayoutField = "button-layout";

    public const string FontSizeField = "font-size";

    public const string BackgroundField = "background";

    public const string WorkspacesField = "workspaces";

    public const string WorkspaceRowsField = "workspace-rows";

    public const string AutoRaiseDelayField = "auto-raise-delay";

    public const string ShellKeysField = "shell-keys";

    public const string PanelSizeField = "panel-size";

    public const string PanelTopField = "panel-top";

    public const string PanelBottomField = "panel-bottom";

    public static IReadOnlyList<string> CompressChoices { get; } = ["lz4 (default)", "lz4", "zstd", "none"];

    public static IReadOnlyList<string> ShellModeChoices { get; } = [ShellModes.Name(ShellMode.Windows), ShellModes.Name(ShellMode.Nested)];

    public static IReadOnlyList<string> PaletteChoices { get; } = ["light", "dark"];

    public static IReadOnlyList<string> FocusModeChoices { get; } =
        [ShellConfig.Name(FocusMode.Click), ShellConfig.Name(FocusMode.Sloppy), ShellConfig.Name(FocusMode.Mouse)];

    public static IReadOnlyList<string> FocusNewWindowsChoices { get; } =
        [ShellConfig.Name(FocusNewWindows.Smart), ShellConfig.Name(FocusNewWindows.Strict)];

    public static IReadOnlyList<string> PlacementChoices { get; } =
        [ShellConfig.Name(PlacementMode.Automatic), ShellConfig.Name(PlacementMode.Pointer), ShellConfig.Name(PlacementMode.Manual)];

    public static IReadOnlyList<string> MouseButtonModifierChoices { get; } = ["Alt", "Super", "Ctrl"];

    public static IReadOnlyList<string> TitlebarActionChoices { get; } =
        Enum.GetValues<TitlebarAction>().Select(ShellConfig.Name).ToArray();

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
    private bool _virtualInput;

    [ObservableProperty]
    private string _captureChord = string.Empty;

    [ObservableProperty]
    private string _sshTimeout = string.Empty;

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

    [ObservableProperty]
    private int _shellModeIndex;

    [ObservableProperty]
    private IReadOnlyList<string> _themeChoices = [ShellSettings.DefaultTheme];

    [ObservableProperty]
    private string _theme = string.Empty;

    [ObservableProperty]
    private string _buttonLayout = string.Empty;

    [ObservableProperty]
    private int _paletteIndex;

    [ObservableProperty]
    private string _fontSize = string.Empty;

    [ObservableProperty]
    private string _background = string.Empty;

    [ObservableProperty]
    private string _workspaces = string.Empty;

    [ObservableProperty]
    private string _workspaceRows = string.Empty;

    [ObservableProperty]
    private string _workspaceNames = string.Empty;

    [ObservableProperty]
    private int _focusModeIndex;

    [ObservableProperty]
    private int _focusNewWindowsIndex;

    [ObservableProperty]
    private bool _raiseOnClick = true;

    [ObservableProperty]
    private bool _autoRaise;

    [ObservableProperty]
    private string _autoRaiseDelay = string.Empty;

    [ObservableProperty]
    private int _placementIndex;

    [ObservableProperty]
    private bool _centerNewWindows = true;

    [ObservableProperty]
    private int _mouseButtonModifierIndex;

    [ObservableProperty]
    private bool _resizeWithRightButton = true;

    [ObservableProperty]
    private int _doubleClickTitlebarIndex;

    [ObservableProperty]
    private int _middleClickTitlebarIndex;

    [ObservableProperty]
    private int _rightClickTitlebarIndex;

    [ObservableProperty]
    private bool _tiling = true;

    [ObservableProperty]
    private bool _topTiling = true;

    [ObservableProperty]
    private string _shellKeys = string.Empty;

    [ObservableProperty]
    private string _panelSize = string.Empty;

    [ObservableProperty]
    private string _panelTop = string.Empty;

    [ObservableProperty]
    private string _panelBottom = string.Empty;

    public string? CaptureChordProblem => ProblemFor(CaptureChordField);

    public string? SshTimeoutProblem => ProblemFor(SshTimeoutField);

    public string? VideoProblem => ProblemFor(VideoField);

    public string? LangProblem => ProblemFor(LangField);

    public string? SocketProblem => ProblemFor(SocketField);

    public string? HotkeysProblem => ProblemFor(HotkeysField);

    public string? ThemeProblem => ProblemFor(ThemeField);

    public string? ButtonLayoutProblem => ProblemFor(ButtonLayoutField);

    public string? FontSizeProblem => ProblemFor(FontSizeField);

    public string? BackgroundProblem => ProblemFor(BackgroundField);

    public string? WorkspacesProblem => ProblemFor(WorkspacesField);

    public string? WorkspaceRowsProblem => ProblemFor(WorkspaceRowsField);

    public string? AutoRaiseDelayProblem => ProblemFor(AutoRaiseDelayField);

    public string? ShellKeysProblem => ProblemFor(ShellKeysField);

    public string? PanelSizeProblem => ProblemFor(PanelSizeField);

    public string? PanelTopProblem => ProblemFor(PanelTopField);

    public string? PanelBottomProblem => ProblemFor(PanelBottomField);

    protected override IReadOnlyList<string> ProblemProperties { get; } =
    [
        nameof(CaptureChordProblem), nameof(SshTimeoutProblem), nameof(VideoProblem), nameof(LangProblem), nameof(SocketProblem), nameof(HotkeysProblem),
        nameof(ThemeProblem), nameof(ButtonLayoutProblem), nameof(FontSizeProblem), nameof(BackgroundProblem), nameof(WorkspacesProblem),
        nameof(WorkspaceRowsProblem), nameof(AutoRaiseDelayProblem), nameof(ShellKeysProblem), nameof(PanelSizeProblem),
        nameof(PanelTopProblem), nameof(PanelBottomProblem),
    ];

    public static IReadOnlyList<string> InstalledThemes()
    {
        try
        {
            return MetacityThemes.Available().Where(static name => name != ShellSettings.DefaultTheme).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

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
        VirtualInput = values.VirtualInput;
        CaptureChord = values.CaptureChord == ConfigValues.DefaultCaptureChord ? string.Empty : values.CaptureChord;
        SshTimeout = Unless(values.SshTimeout, ConfigValues.DefaultSshTimeout);
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
        LoadShell(values.Shell, values.ShellSettings, values.Panel);
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

        var sshTimeout = Whole(SshTimeout, ConfigValues.DefaultSshTimeout, 1, ConfigValues.MaxSshTimeout, SshTimeoutField,
            $"Use 1 to {ConfigValues.MaxSshTimeout}, in seconds.");
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

        var shell = ValidateShell();
        var panel = ValidatePanel();
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
            sshTimeout,
            chord,
            hotkeys,
            Shell: ShellModeIndex == 1 ? ShellMode.Nested : ShellMode.Windows,
            ShellSettings: shell,
            Panel: panel,
            VirtualInput: VirtualInput);
    }

    private void LoadShell(ShellMode mode, ShellSettings shell, PanelSettings panel)
    {
        var defaults = new ShellSettings();
        ShellModeIndex = mode == ShellMode.Nested ? 1 : 0;
        ThemeChoices = [ShellSettings.DefaultTheme, .. InstalledThemes()];
        Theme = Unless(shell.Theme, defaults.Theme);
        ButtonLayout = Unless(shell.ButtonLayout, defaults.ButtonLayout);
        PaletteIndex = Math.Max(0, IndexOf(PaletteChoices, shell.Palette));
        FontSize = shell.FontSize == defaults.FontSize ? string.Empty : shell.FontSize.ToString(CultureInfo.InvariantCulture);
        Background = Unless(shell.Background, defaults.Background);
        Workspaces = Unless(shell.Workspaces, defaults.Workspaces);
        WorkspaceRows = Unless(shell.WorkspaceRows, defaults.WorkspaceRows);
        WorkspaceNames = string.Join(", ", shell.WorkspaceNames);
        FocusModeIndex = Math.Max(0, IndexOf(FocusModeChoices, ShellConfig.Name(shell.FocusMode)));
        FocusNewWindowsIndex = Math.Max(0, IndexOf(FocusNewWindowsChoices, ShellConfig.Name(shell.FocusNewWindows)));
        RaiseOnClick = shell.RaiseOnClick;
        AutoRaise = shell.AutoRaise;
        AutoRaiseDelay = Unless(shell.AutoRaiseDelay, defaults.AutoRaiseDelay);
        PlacementIndex = Math.Max(0, IndexOf(PlacementChoices, ShellConfig.Name(shell.Placement)));
        CenterNewWindows = shell.CenterNewWindows;
        MouseButtonModifierIndex = Math.Max(0, IndexOf(MouseButtonModifierChoices, shell.MouseButtonModifier));
        ResizeWithRightButton = shell.ResizeWithRightButton;
        DoubleClickTitlebarIndex = Math.Max(0, IndexOf(TitlebarActionChoices, ShellConfig.Name(shell.DoubleClickTitlebar)));
        MiddleClickTitlebarIndex = Math.Max(0, IndexOf(TitlebarActionChoices, ShellConfig.Name(shell.MiddleClickTitlebar)));
        RightClickTitlebarIndex = Math.Max(0, IndexOf(TitlebarActionChoices, ShellConfig.Name(shell.RightClickTitlebar)));
        Tiling = shell.Tiling;
        TopTiling = shell.TopTiling;
        ShellKeys = ShellKeyLines.Render(shell.Keys);
        PanelSize = Unless(panel.Size, PanelSettings.DefaultSize);
        PanelTop = string.Join(", ", panel.Top);
        PanelBottom = string.Join(", ", panel.Bottom);
    }

    private ShellSettings ValidateShell()
    {
        var defaults = new ShellSettings();
        var theme = Blank(Theme) ?? defaults.Theme;
        if (theme != ShellSettings.DefaultTheme && !ThemeChoices.Contains(theme))
        {
            var installed = ThemeChoices.Where(static name => name != ShellSettings.DefaultTheme).ToList();
            Complain(ThemeField, installed.Count == 0
                ? $"Use a theme name; the bundled theme is {ShellSettings.DefaultTheme} and no other theme is installed."
                : $"Use a theme name; the bundled theme is {ShellSettings.DefaultTheme} and the installed ones are {string.Join(", ", installed)}.");
        }

        var buttonLayout = Blank(ButtonLayout) ?? defaults.ButtonLayout;
        if (!ShellConfig.IsButtonLayout(buttonLayout))
        {
            Complain(ButtonLayoutField, $"Use {ShellConfig.ButtonNames} around one ':', such as menu:minimize,maximize,close.");
        }

        var fontSize = defaults.FontSize;
        if (Blank(FontSize) is { } fontSizeText)
        {
            if (!double.TryParse(fontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize) || fontSize <= 0 || !double.IsFinite(fontSize))
            {
                Complain(FontSizeField, "Use a positive number, such as 13.");
                fontSize = defaults.FontSize;
            }
        }

        var background = defaults.Background;
        if (Blank(Background) is { } backgroundText)
        {
            if (ShellColor.Normalize(backgroundText) is { } color)
            {
                background = color;
            }
            else
            {
                Complain(BackgroundField, $"Use a color as {ShellColor.Format}, such as {defaults.Background}.");
            }
        }

        var workspaces = Whole(Workspaces, defaults.Workspaces, 1, ShellConfig.MaxWorkspaces, WorkspacesField, $"Use 1 to {ShellConfig.MaxWorkspaces}.");
        var workspaceRows = Whole(WorkspaceRows, Math.Min(defaults.WorkspaceRows, workspaces), 1, workspaces, WorkspaceRowsField,
            workspaces == 1 ? "Use 1; there is one workspace." : $"Use 1 to {workspaces}, the number of workspaces.");
        var autoRaiseDelay = Whole(AutoRaiseDelay, defaults.AutoRaiseDelay, 0, int.MaxValue, AutoRaiseDelayField,
            "Use a whole number of milliseconds, 0 or more.");
        var keys = ShellKeyLines.Parse(ShellKeys, out var keysProblem);
        if (keysProblem is not null)
        {
            Complain(ShellKeysField, keysProblem);
        }

        return new ShellSettings(
            theme,
            buttonLayout,
            Choice(PaletteChoices, PaletteIndex, defaults.Palette),
            fontSize,
            background,
            workspaces,
            workspaceRows,
            Names(WorkspaceNames),
            ShellConfig.ParseFocusMode(Choice(FocusModeChoices, FocusModeIndex, string.Empty)) ?? defaults.FocusMode,
            ShellConfig.ParseFocusNewWindows(Choice(FocusNewWindowsChoices, FocusNewWindowsIndex, string.Empty)) ?? defaults.FocusNewWindows,
            ShellConfig.ParsePlacement(Choice(PlacementChoices, PlacementIndex, string.Empty)) ?? defaults.Placement,
            CenterNewWindows,
            RaiseOnClick,
            AutoRaise,
            autoRaiseDelay,
            Choice(MouseButtonModifierChoices, MouseButtonModifierIndex, defaults.MouseButtonModifier),
            ResizeWithRightButton,
            ShellConfig.ParseTitlebarAction(Choice(TitlebarActionChoices, DoubleClickTitlebarIndex, string.Empty)) ?? defaults.DoubleClickTitlebar,
            ShellConfig.ParseTitlebarAction(Choice(TitlebarActionChoices, MiddleClickTitlebarIndex, string.Empty)) ?? defaults.MiddleClickTitlebar,
            ShellConfig.ParseTitlebarAction(Choice(TitlebarActionChoices, RightClickTitlebarIndex, string.Empty)) ?? defaults.RightClickTitlebar,
            Tiling,
            TopTiling,
            keys);
    }

    private PanelSettings ValidatePanel()
    {
        var size = Whole(PanelSize, PanelSettings.DefaultSize, PanelConfig.MinSize, PanelConfig.MaxSize, PanelSizeField,
            $"Use {PanelConfig.MinSize} to {PanelConfig.MaxSize}, in pixels.");
        return new PanelSettings(size, Applets(PanelTop, PanelTopField), Applets(PanelBottom, PanelBottomField));
    }

    private IReadOnlyList<string> Applets(string text, string field)
    {
        var names = Names(text);
        foreach (var name in names)
        {
            if (PanelApplets.ParseApplet(name) is null)
            {
                Complain(field, $"'{name}' is not an applet. The applets are {PanelApplets.AppletNames}, separated by commas.");
                break;
            }
        }

        return names;
    }

    private int Whole(string text, int fallback, int min, int max, string field, string problem)
    {
        if (Blank(text) is not { } value)
        {
            return fallback;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max)
        {
            return number;
        }

        Complain(field, problem);
        return fallback;
    }

    private static string Choice(IReadOnlyList<string> choices, int index, string fallback) =>
        index >= 0 && index < choices.Count ? choices[index] : fallback;

    private static string Unless(string value, string fallback) => value == fallback ? string.Empty : value;

    private static string Unless(int value, int fallback) => value == fallback ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Names(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
