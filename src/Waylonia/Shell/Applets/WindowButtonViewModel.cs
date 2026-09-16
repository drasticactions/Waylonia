using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Shell.Applets;

internal sealed partial class WindowButtonViewModel : ObservableObject
{
    private readonly IPanelCommands _commands;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isFocused;

    [ObservableProperty]
    private bool _isMinimized;

    [ObservableProperty]
    private bool _demandsAttention;

    [ObservableProperty]
    private string? _session;

    [ObservableProperty]
    private Bitmap? _icon;

    public WindowButtonViewModel(IPanelCommands commands, PanelWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(window);
        _commands = commands;
        Id = window.Id;
        Update(window);
    }

    public long Id { get; }

    public static string TitleOf(PanelWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Title.Length == 0 ? window.AppId : window.Title;
    }

    public void Update(PanelWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Title = TitleOf(window);
        IsFocused = window.Focused;
        IsMinimized = window.Minimized;
        DemandsAttention = window.DemandsAttention;
        Session = window.Session;
        Icon = IconImages.Load(window.Icon);
    }

    [RelayCommand]
    private void Activate()
    {
        if (IsFocused && !IsMinimized)
        {
            _commands.MinimizeWindow(Id);
        }
        else
        {
            _commands.FocusWindow(Id);
        }
    }

    [RelayCommand]
    private void Menu() => _commands.ShowWindowMenu(Id);
}
