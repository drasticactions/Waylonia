namespace Waylonia.Shell;

internal sealed record PanelIcons(
    string? Sessions = null,
    string? Settings = null,
    string? Disconnect = null,
    string? Quit = null,
    string? Session = null,
    Func<string, string?>? Place = null)
{
    public static PanelIcons None { get; } = new();

    public string? PlaceIcon(string path) => Place?.Invoke(path);
}
