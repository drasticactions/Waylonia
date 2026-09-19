using Avalonia.Controls;

namespace Waylonia.Shell;

internal interface IShellHost
{
    ShellView View { get; }

    TopLevel? TopLevel { get; }

    bool CanFullScreen { get; }

    void ToggleFullScreen();

    void SetTitle(string title);

    void Present();

    Task CloseAsync();

    event Action? ActivatedOnHost;

    event Action? DeactivatedOnHost;
}
