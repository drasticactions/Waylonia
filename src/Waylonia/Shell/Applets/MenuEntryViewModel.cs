using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace Waylonia.Shell.Applets;

internal sealed class MenuEntryViewModel
{
    public const string SeparatorHeader = "-";

    public MenuEntryViewModel(string header, IRelayCommand? command = null, bool isEnabled = true, string? toolTip = null, string? icon = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        Header = header;
        Command = command;
        IsEnabled = isEnabled;
        ToolTip = toolTip;
        IconPath = icon;
    }

    private MenuEntryViewModel()
    {
        Header = SeparatorHeader;
        IsSeparator = true;
        IsEnabled = false;
    }

    public string Header { get; }

    public ObservableCollection<MenuEntryViewModel> Children { get; } = [];

    public IRelayCommand? Command { get; }

    public bool IsSeparator { get; }

    public bool IsEnabled { get; }

    public string? ToolTip { get; }

    public string? IconPath { get; }

    public Bitmap? Icon => IconImages.Load(IconPath);

    public static MenuEntryViewModel Separator() => new();

    public MenuEntryViewModel WithChildren(IEnumerable<MenuEntryViewModel> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        foreach (var child in children)
        {
            Children.Add(child);
        }

        return this;
    }
}
