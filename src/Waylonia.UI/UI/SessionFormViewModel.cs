using CommunityToolkit.Mvvm.ComponentModel;
using Waylonia.Cli;
using Waylonia.Sessions;

namespace Waylonia.UI;

internal sealed partial class SessionFormViewModel : FormViewModel
{
    public const string NameField = "name";

    public const string SshField = "ssh";

    public const string UserField = "user";

    public const string CommandField = "command";

    public const string VideoField = "video";

    public const string LangField = "lang";

    public const string DesktopSizeField = "desktop-size";

    public const string HotkeysField = "hotkeys";

    public static IReadOnlyList<string> CompressChoices { get; } = ["default", "lz4", "zstd", "none"];

    public static IReadOnlyList<string> DesktopChoices { get; } =
        ["none", .. DesktopRecipes.All.Select(static recipe => recipe.Name), "custom"];

    [ObservableProperty]
    private string _name = string.Empty;

    private readonly Dictionary<DestinationKind, IReadOnlyList<string>> _listed = [];
    private IReadOnlyList<DestinationKind> _available = [];
    private DestinationKind _kind = DestinationKinds.Ssh;

    [ObservableProperty]
    private string _ssh = string.Empty;

    [ObservableProperty]
    private string _target = string.Empty;

    [ObservableProperty]
    private string _user = string.Empty;

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

    public System.Collections.ObjectModel.ObservableCollection<string> Targets { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<DestinationKind> Kinds { get; } = [DestinationKinds.Ssh];

    public IReadOnlyList<DestinationKind> AvailableKinds
    {
        get => _available;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _available = value;
            RefreshKinds();
        }
    }

    public DestinationKind? Kind
    {
        get => _kind;
        set
        {
            if (value is null || value == _kind)
            {
                return;
            }

            _kind = value;
            RefreshKinds();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSsh));
            OnPropertyChanged(nameof(IsTarget));
            OnPropertyChanged(nameof(ShowsUser));
            OnPropertyChanged(nameof(TargetCaption));
            OnPropertyChanged(nameof(TargetPlaceholder));
            RefreshTargets();
        }
    }

    public bool IsSsh => _kind.IsSsh;

    public bool IsTarget => !_kind.IsSsh;

    public bool ShowsUser => _kind.TakesUser;

    public bool ShowsKind => Kinds.Count > 1;

    public string TargetCaption => _kind.Caption;

    public string? TargetPlaceholder => _kind.Placeholder;

    public void SetListed(DestinationKind kind, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(names);
        _listed[kind] = names;
        if (kind == _kind)
        {
            RefreshTargets();
        }
    }

    private void RefreshKinds()
    {
        var wanted = new List<DestinationKind> { DestinationKinds.Ssh };
        foreach (var kind in DestinationKinds.Targets)
        {
            if (_available.Contains(kind) || kind == _kind)
            {
                wanted.Add(kind);
            }
        }

        if (wanted.SequenceEqual(Kinds))
        {
            return;
        }

        var kept = _kind;
        Kinds.Clear();
        foreach (var kind in wanted)
        {
            Kinds.Add(kind);
        }

        _kind = kept;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(ShowsKind));
    }

    private void RefreshTargets()
    {
        var target = Target;
        Targets.Clear();
        foreach (var name in _listed.GetValueOrDefault(_kind) ?? [])
        {
            Targets.Add(name);
        }

        Target = target;
    }

    public string? NameProblem => ProblemFor(NameField);

    public string? SshProblem => ProblemFor(SshField);

    public string? UserProblem => ProblemFor(UserField);

    public string? CommandProblem => ProblemFor(CommandField);

    public string? VideoProblem => ProblemFor(VideoField);

    public string? LangProblem => ProblemFor(LangField);

    public string? DesktopSizeProblem => ProblemFor(DesktopSizeField);

    public string? HotkeysProblem => ProblemFor(HotkeysField);

    protected override IReadOnlyList<string> ProblemProperties { get; } =
    [
        nameof(NameProblem), nameof(SshProblem), nameof(UserProblem), nameof(CommandProblem), nameof(VideoProblem),
        nameof(LangProblem), nameof(DesktopSizeProblem), nameof(HotkeysProblem),
    ];

    public void Load(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = profile.Name;
        var kind = DestinationKinds.For(profile.Ssh);
        kind.TryParse(profile.Ssh, out var target);
        Target = target;
        Kind = kind;
        Ssh = kind.IsSsh ? profile.Ssh : string.Empty;
        User = profile.User ?? string.Empty;
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
        Hotkeys = HotkeyLines.Render(profile.Hotkeys);
        ClearProblems();
    }

    public void Clear() => Load(new SessionProfile(string.Empty, string.Empty));

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

        string? ssh;
        if (IsTarget)
        {
            var target = Target.Trim();
            ssh = _kind.For(target);
            if (_kind.NameProblem(target) is not null)
            {
                Complain(SshField, _kind.FormHint!);
            }
        }
        else
        {
            ssh = Blank(Ssh);
            if (ssh is null)
            {
                Complain(SshField, "Give the ssh destination, such as user@host.");
            }
            else if (DestinationKinds.For(ssh) is { IsSsh: false } other)
            {
                Complain(SshField, $"Choose {other.Label} as the kind to reach {other.Noun}.");
            }
        }

        var user = ShowsUser ? Blank(User) : null;
        if (user is not null && SessionStore.UserProblem(user) is not null)
        {
            Complain(UserField, "Use the account name inside the instance, or leave it empty for uid 1000.");
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
        if (size is not null && RunRules.ParseSize(size) is null)
        {
            Complain(DesktopSizeField, "Use WIDTHxHEIGHT, such as 1920x1080.");
        }

        var hotkeys = HotkeyLines.Parse(Hotkeys, name, out var hotkeysProblem);
        if (hotkeysProblem is not null)
        {
            Complain(HotkeysField, hotkeysProblem);
        }

        if (HasProblems)
        {
            return null;
        }

        var autostart = HotkeyLines.Lines(Autostart);
        var env = HotkeyLines.Lines(DesktopEnv);
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
            hotkeys is { Count: > 0 } ? hotkeys : null,
            user);
    }

}
