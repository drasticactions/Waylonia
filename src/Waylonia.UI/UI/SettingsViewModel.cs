using System.Collections.ObjectModel;
using Basin.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.UI;

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly string? _path;
    private readonly ConfigValues _running;
    private readonly BasinLogger _log;
    private readonly Action<Config>? _applied;
    private ConfigValues _loaded = new();

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _problem;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private IReadOnlyList<string> _restartKeys = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _canSave;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopCommand))]
    private DesktopFormViewModel? _selectedDesktop;

    public SettingsViewModel(string? path, ConfigValues running, BasinLogger log, Action<Config>? applied = null)
    {
        ArgumentNullException.ThrowIfNull(running);
        _path = path;
        _running = running;
        _log = log;
        _applied = applied;
        Reload();
    }

    public SettingsFormViewModel Form { get; } = new();

    public ObservableCollection<DesktopFormViewModel> Desktops { get; } = [];

    public ConfigValues Loaded => _loaded;

    [RelayCommand]
    public void Reload()
    {
        Problem = null;
        Note = null;
        if (_path is not { } path)
        {
            StatusText = "No settings file";
            Problem = "Waylonia started with --config false, so there's no settings file to edit.";
            CanSave = false;
            Load(new ConfigValues());
            return;
        }

        StatusText = path;
        if (ParseProblem(path, _log) is { } parseProblem)
        {
            Problem = parseProblem;
            CanSave = false;
        }
        else
        {
            CanSave = true;
        }

        Load(Config.Load(WayloniaPaths.ConfigOnly(path), _log).Values);
        RestartKeys = _running.RestartKeysChanged(_loaded);
        Note = RestartNote(RestartKeys);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveSettings() => Save();

    public bool Save()
    {
        Problem = null;
        Note = null;
        if (_path is not { } path)
        {
            return false;
        }

        var values = Form.Validate();
        var desktops = new List<DesktopProfile>();
        foreach (var form in Desktops)
        {
            if (form.Validate() is not { } profile)
            {
                SelectedDesktop = form;
                Problem = $"Check the desktop {form.Label}. One of its fields needs fixing.";
                return false;
            }

            if (desktops.Any(other => other.Name == profile.Name))
            {
                form.Complain(DesktopFormViewModel.NameField, $"Two desktops are named {profile.Name}. Use another name.");
                SelectedDesktop = form;
                Problem = $"Check the desktop {form.Label}. One of its fields needs fixing.";
                return false;
            }

            desktops.Add(profile);
        }

        if (values is null)
        {
            Problem = "Some fields need fixing. Look for the messages under them.";
            return false;
        }

        values = values with { Desktops = desktops };
        if (ConfigWriter.Save(path, values) is { } error)
        {
            _log.Warn($"settings: saving {path} failed, {error}");
            Problem = $"Couldn't save your settings. {error}";
            return false;
        }

        var config = Config.Load(WayloniaPaths.ConfigOnly(path), _log);
        _loaded = config.Values;
        StatusText = path;
        RestartKeys = _running.RestartKeysChanged(_loaded);
        Note = RestartNote(RestartKeys) is { } restart ? $"Saved. {restart}" : "Saved.";
        _applied?.Invoke(config);
        return true;
    }

    [RelayCommand]
    public void NewDesktop()
    {
        var form = new DesktopFormViewModel();
        Desktops.Add(form);
        SelectedDesktop = form;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDesktop))]
    public void DeleteDesktop()
    {
        if (SelectedDesktop is not { } form)
        {
            return;
        }

        var index = Desktops.IndexOf(form);
        Desktops.Remove(form);
        SelectedDesktop = Desktops.Count == 0 ? null : Desktops[Math.Min(index, Desktops.Count - 1)];
    }

    public bool CanDeleteDesktop => SelectedDesktop is not null;

    private void Load(ConfigValues values)
    {
        _loaded = values;
        Form.Load(values);
        Desktops.Clear();
        foreach (var profile in values.Desktops)
        {
            var form = new DesktopFormViewModel();
            form.Load(profile);
            Desktops.Add(form);
        }

        SelectedDesktop = Desktops.FirstOrDefault();
    }

    private static string? ParseProblem(string path, BasinLogger log)
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn(error, $"settings: reading {path} failed");
            return $"Couldn't read {path}. {error.Message}";
        }

        return Cli.TomlDocument.Parse(text, out var error2) is null
            ? $"Your settings file has a mistake, so you can't save here until it's fixed. Fix {path} in a text editor, then choose Revert. {error2}"
            : null;
    }

    private static string? RestartNote(IReadOnlyList<string> keys) =>
        keys.Count == 0 ? null : $"Restart Waylonia to apply: {string.Join(", ", keys.Select(RestartLabel))}.";

    private static string RestartLabel(string key) => key switch
    {
        "xwayland" => "Start XWayland",
        "shell" => "Window mode",
        "tray" => "Tray icon",
        "clipboard" => "Share the clipboard",
        "drag" => "Drag and drop",
        "follow-cursor" => "Open windows where the pointer is",
        "virtual-input" => "Virtual keyboard and pointer",
        "socket" => "Wayland socket",
        "command" => "Startup app",
        _ => key,
    };
}
