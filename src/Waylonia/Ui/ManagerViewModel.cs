using System.Collections.ObjectModel;
using Avalonia.Threading;
using Basin.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Waylonia.Sessions;

namespace Waylonia.Ui;

internal sealed partial class ManagerViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly SessionRegistry _registry;
    private readonly BasinLogger _log;
    private readonly Func<SessionProfile, Task<string?>> _connect;
    private readonly Func<string, Task> _disconnect;
    private readonly Action? _openSettings;
    private SessionCatalog _catalog = SessionCatalog.Empty;
    private string? _editing;
    private bool _loading;
    private bool _detached;

    [ObservableProperty]
    private SessionRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _statusText = "New session";

    [ObservableProperty]
    private string? _problem;

    [ObservableProperty]
    private string _connectLabel = "Connect";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSessionCommand))]
    private bool _canDelete;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    private bool _canConnect = true;

    public ManagerViewModel(
        SessionStore store,
        SessionRegistry registry,
        BasinLogger log,
        Func<SessionProfile, Task<string?>> connect,
        Func<string, Task> disconnect,
        Action? openSettings = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(disconnect);
        _store = store;
        _registry = registry;
        _log = log;
        _connect = connect;
        _disconnect = disconnect;
        _openSettings = openSettings;
        _registry.Changed += OnRegistryChanged;
        Reload();
    }

    public SessionFormViewModel Form { get; } = new();

    public ObservableCollection<SessionRowViewModel> Rows { get; } = [];

    public string? Selected => _editing;

    public void Detach()
    {
        _detached = true;
        _registry.Changed -= OnRegistryChanged;
    }

    public void Reload()
    {
        _catalog = _store.Load(_log);
        var rows = new List<SessionRowViewModel>();
        foreach (var profile in _catalog.Profiles)
        {
            rows.Add(new SessionRowViewModel(profile.Name, Detail(profile.Name, profile.Ssh), profile, null));
        }

        foreach (var broken in _catalog.Broken)
        {
            rows.Add(new SessionRowViewModel(broken.Name, $"broken: {broken.Error}", null, broken));
        }

        foreach (var session in _registry.Sessions)
        {
            if (session.Settings.AdHoc && rows.All(row => row.Name != session.Name))
            {
                rows.Add(new SessionRowViewModel(session.Name, Detail(session.Name, session.Ssh), null, null));
            }
        }

        _loading = true;
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        SelectedRow = rows.FirstOrDefault(row => row.Name == _editing);
        _loading = false;
        ShowStatus(SelectedRow is null && rows.All(row => row.Name != _editing) ? null : _editing);
    }

    public void Select(string name) => SelectedRow = Rows.FirstOrDefault(row => row.Name == name);

    [RelayCommand]
    public void NewSession()
    {
        _loading = true;
        SelectedRow = null;
        _loading = false;
        _editing = null;
        Form.Clear();
        Problem = null;
        ShowStatus(null);
    }

    [RelayCommand]
    private void SaveSession() => Save();

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings() => _openSettings?.Invoke();

    public bool CanOpenSettings => _openSettings is not null;

    public bool Save()
    {
        Problem = null;
        if (Form.Validate() is not { } profile)
        {
            return false;
        }

        if (_editing != profile.Name && _catalog.Find(profile.Name) is not null)
        {
            Form.Complain(SessionFormViewModel.NameField, $"A session named {profile.Name} exists. Use another name.");
            return false;
        }

        if (_registry.Get(_editing ?? profile.Name) is { IsLive: true })
        {
            Problem = $"{_editing ?? profile.Name} is connected. Disconnect it before you change it.";
            return false;
        }

        try
        {
            if (_editing is not null && _editing != profile.Name)
            {
                _store.Delete(_editing);
            }

            _store.Save(profile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Problem = $"The session file was not written: {error.Message}";
            return false;
        }

        _editing = profile.Name;
        Reload();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public void DeleteSession()
    {
        if (_editing is not { } name)
        {
            return;
        }

        if (_registry.Get(name) is { IsLive: true })
        {
            Problem = $"{name} is connected. Disconnect it before you delete it.";
            return;
        }

        try
        {
            _store.Delete(name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Problem = $"The session file was not deleted: {error.Message}";
            return;
        }

        _ = _registry.RemoveAsync(name);
        _editing = null;
        Reload();
        NewSession();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task ToggleConnectionAsync()
    {
        if (_editing is not { } name)
        {
            if (!Save() || _editing is null)
            {
                return;
            }

            name = _editing;
        }

        if (_registry.Get(name) is { IsLive: true })
        {
            await _disconnect(name);
            Reload();
            return;
        }

        SessionProfile profile;
        if (_catalog.Find(name) is { } saved)
        {
            profile = saved;
            if (Form.Validate() is { } edited && edited != saved)
            {
                if (!Save())
                {
                    return;
                }

                profile = edited;
            }
        }
        else if (_registry.Get(name)?.Settings is { } adHoc)
        {
            profile = new SessionProfile(adHoc.Name, adHoc.Ssh);
        }
        else
        {
            return;
        }

        if (await _connect(profile) is { } failure)
        {
            Problem = failure;
        }

        Reload();
    }

    partial void OnSelectedRowChanged(SessionRowViewModel? value)
    {
        if (!_loading && value is not null)
        {
            Edit(value);
        }
    }

    private void Edit(SessionRowViewModel row)
    {
        _editing = row.Name;
        if (row.Profile is { } profile)
        {
            Form.Load(profile);
        }
        else if (_registry.Get(row.Name)?.Settings is { } settings)
        {
            Form.Load(new SessionProfile(settings.Name, settings.Ssh, settings.Command));
        }
        else
        {
            Form.Load(new SessionProfile(row.Name, string.Empty));
        }

        Problem = row.Broken is { } broken ? $"This file does not load: {broken.Error}" : null;
        ShowStatus(row.Name);
    }

    private string Detail(string name, string ssh)
    {
        var session = _registry.Get(name);
        var status = session?.Status ?? SessionStatus.Disconnected;
        var text = $"{SessionMenu.Glyph(status)} {ssh}";
        return session?.LastError is { } error && status == SessionStatus.Disconnected ? $"{text}\n{error}" : text;
    }

    private void ShowStatus(string? name)
    {
        var session = name is null ? null : _registry.Get(name);
        var live = session is { IsLive: true };
        ConnectLabel = live ? "Disconnect" : "Connect";
        CanConnect = name is null || _catalog.Broken.All(broken => broken.Name != name);
        CanDelete = name is not null && !live;
        if (session is null)
        {
            StatusText = name is null ? "New session" : "Not connected";
            return;
        }

        var text = $"{SessionMenu.Glyph(session.Status)} {session.StatusText}";
        if (session.LastError is { } error && session.Status == SessionStatus.Disconnected)
        {
            text += $"\n{error}";
        }

        var tail = session.RecentOutput;
        if (tail.Count > 0 && session.Status == SessionStatus.Disconnected)
        {
            text += "\n" + string.Join('\n', tail.TakeLast(5));
        }

        StatusText = text;
    }

    private void OnRegistryChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_detached)
        {
            Reload();
        }
    });
}
