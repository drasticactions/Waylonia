using System.Collections.ObjectModel;
using Basin.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Ui;

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
            StatusText = "The config file is off.";
            Problem = "This run skips the config file (--config false), so there is nothing to edit.";
            CanSave = false;
            Load(new ConfigValues());
            return;
        }

        StatusText = path;
        if (ParseProblem(path) is { } parseProblem)
        {
            Problem = parseProblem;
            CanSave = false;
        }
        else
        {
            CanSave = true;
        }

        Load(Config.Load(false, path, _log).Values);
        Note = RestartNote(_running.RestartKeysChanged(_loaded));
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
                Problem = $"The desktop {form.Label} has a problem. See its fields.";
                return false;
            }

            if (desktops.Any(other => other.Name == profile.Name))
            {
                form.Complain(DesktopFormViewModel.NameField, $"Two desktops are named {profile.Name}. Use another name.");
                SelectedDesktop = form;
                Problem = $"The desktop {form.Label} has a problem. See its fields.";
                return false;
            }

            desktops.Add(profile);
        }

        if (values is null)
        {
            Problem = "Some fields have a problem. See the messages under them.";
            return false;
        }

        values = values with { Desktops = desktops };
        if (ConfigWriter.Save(path, values) is { } error)
        {
            Problem = $"The config file was not written: {error}";
            return false;
        }

        var config = Config.Load(false, path, _log);
        _loaded = config.Values;
        StatusText = path;
        Note = RestartNote(_running.RestartKeysChanged(_loaded)) ?? "Saved.";
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

    private static string? ParseProblem(string path)
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
            return $"{path} cannot be read: {error.Message}";
        }

        return Cli.TomlDocument.Parse(text, out var error2) is null
            ? $"{path} did not parse: {error2}. Fix the file by hand; nothing is saved until it loads."
            : null;
    }

    private static string? RestartNote(IReadOnlyList<string> keys) => keys.Count switch
    {
        0 => null,
        1 => $"Saved. {keys[0]} takes effect when Waylonia restarts.",
        _ => $"Saved. {string.Join(", ", keys.Take(keys.Count - 1))} and {keys[^1]} take effect when Waylonia restarts.",
    };
}
