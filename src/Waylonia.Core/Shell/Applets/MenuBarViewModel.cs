using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Waylonia.Sessions;

namespace Waylonia.Shell.Applets;

internal sealed class MenuBarViewModel
{
    public const string ApplicationsHeader = "Applications";

    public const string PlacesHeader = "Places";

    public const string SystemHeader = "System";

    public const string NoApplications = "No applications";

    public const string NoSession = "No session connected";

    public const string ManagerLabel = "Sessions…";

    public const string SettingsLabel = "Settings…";

    public const string QuitLabel = "Quit";

    private readonly PanelModel _model;

    public MenuBarViewModel(PanelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        Applications = new MenuEntryViewModel(ApplicationsHeader);
        Places = new MenuEntryViewModel(PlacesHeader);
        System = new MenuEntryViewModel(SystemHeader);
        Roots = [Applications, Places, System];
        Replace(Applications, BuildApplications());
        Replace(Places, BuildPlaces());
        Replace(System, BuildSystem());
        model.PropertyChanged += OnModelChanged;
    }

    public MenuEntryViewModel Applications { get; }

    public MenuEntryViewModel Places { get; }

    public MenuEntryViewModel System { get; }

    public IReadOnlyList<MenuEntryViewModel> Roots { get; }

    public static string PlaceToolTip(string session, string path) => $"Opens {path} with xdg-open on {session}";

    public static string DisconnectLabel(string session) => $"Disconnect {session}";

    private IPanelCommands Commands => _model.Commands;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PanelModel.Applications):
                Replace(Applications, BuildApplications());
                break;
            case nameof(PanelModel.Sessions):
                Replace(Places, BuildPlaces());
                Replace(System, BuildSystem());
                break;
            case nameof(PanelModel.SettingsAvailable):
                Replace(System, BuildSystem());
                break;
            case nameof(PanelModel.Icons):
                Replace(Places, BuildPlaces());
                Replace(System, BuildSystem());
                break;
        }
    }

    private static void Replace(MenuEntryViewModel root, IEnumerable<MenuEntryViewModel> children)
    {
        root.Children.Clear();
        foreach (var child in children)
        {
            root.Children.Add(child);
        }
    }

    private IEnumerable<MenuEntryViewModel> BuildApplications()
    {
        if (_model.Applications.Count == 0)
        {
            return [new MenuEntryViewModel(NoApplications, isEnabled: false)];
        }

        return _model.Applications.Select(Convert);
    }

    private MenuEntryViewModel Convert(ApplicationMenuItem item)
    {
        if (item.Separator)
        {
            return MenuEntryViewModel.Separator();
        }

        if (item.Children is { } children)
        {
            return new MenuEntryViewModel(item.Label, icon: item.Icon).WithChildren(children.Select(Convert));
        }

        if (item.Invoke is { } invoke)
        {
            return new MenuEntryViewModel(item.Label, new RelayCommand(invoke));
        }

        if (item.Command is { } command)
        {
            var session = item.Session;
            var label = item.Label;
            return new MenuEntryViewModel(label, new RelayCommand(() => Commands.Launch(session, label, command)), icon: item.Icon);
        }

        return new MenuEntryViewModel(item.Label, isEnabled: false);
    }

    private IEnumerable<MenuEntryViewModel> BuildPlaces()
    {
        var connected = Connected().ToList();
        if (connected.Count == 0)
        {
            return [new MenuEntryViewModel(NoSession, isEnabled: false)];
        }

        var icons = _model.Icons;
        return connected.Select(session => new MenuEntryViewModel(session, icon: icons.Session).WithChildren(
            Shell.Places.Entries.Select(place => new MenuEntryViewModel(
                place.Label,
                new RelayCommand(() => Commands.OpenPlace(session, place.Path)),
                toolTip: PlaceToolTip(session, place.Path),
                icon: icons.PlaceIcon(place.Path)))));
    }

    private IEnumerable<MenuEntryViewModel> BuildSystem()
    {
        var icons = _model.Icons;
        var entries = new List<MenuEntryViewModel>
        {
            new(ManagerLabel, new RelayCommand(Commands.OpenManager), icon: icons.Sessions),
        };
        if (_model.SettingsAvailable)
        {
            entries.Add(new MenuEntryViewModel(SettingsLabel, new RelayCommand(Commands.OpenSettings), icon: icons.Settings));
        }

        var live = Live().ToList();
        if (live.Count > 0)
        {
            entries.Add(MenuEntryViewModel.Separator());
            entries.AddRange(live.Select(session => new MenuEntryViewModel(
                DisconnectLabel(session),
                new RelayCommand(() => Commands.Disconnect(session)),
                icon: icons.Disconnect)));
        }

        entries.Add(MenuEntryViewModel.Separator());
        entries.Add(new MenuEntryViewModel(QuitLabel, new RelayCommand(Commands.Quit), icon: icons.Quit));
        return entries;
    }

    private IEnumerable<string> Connected() =>
        _model.Sessions.Where(session => session.Status == SessionStatus.Connected).Select(session => session.Name);

    private IEnumerable<string> Live() =>
        _model.Sessions.Where(session => session.Status != SessionStatus.Disconnected).Select(session => session.Name);
}
