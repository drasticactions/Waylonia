using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Shell.Applets;

internal sealed partial class KeyboardViewModel : ObservableObject
{
    private readonly PanelModel _model;

    [ObservableProperty]
    private bool _open;

    [ObservableProperty]
    private string? _iconPath;

    public KeyboardViewModel(PanelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _open = model.SoftKeyboardOpen;
        _iconPath = model.Icons.Keyboard;
        model.PropertyChanged += OnModelChanged;
    }

    [RelayCommand]
    private void Toggle() => _model.Commands.ToggleSoftKeyboard();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PanelModel.SoftKeyboardOpen):
                Open = _model.SoftKeyboardOpen;
                break;
            case nameof(PanelModel.Icons):
                IconPath = _model.Icons.Keyboard;
                break;
        }
    }
}
