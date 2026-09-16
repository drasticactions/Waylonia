using CommunityToolkit.Mvvm.ComponentModel;

namespace Waylonia.Shell;

internal sealed partial class SwitcherEntry : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public SwitcherEntry(string title, string? session, string? icon = null)
    {
        Title = title;
        Session = session;
        Icon = Applets.IconImages.Load(icon);
    }

    public Avalonia.Media.Imaging.Bitmap? Icon { get; }

    public string Title { get; }

    public string? Session { get; }

    public string Subtitle => Session ?? string.Empty;
}
