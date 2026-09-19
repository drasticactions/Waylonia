using Basin.Shell.Nested;
namespace Waylonia.Shell;

internal sealed class PanelCommands : IPanelCommands
{
    private readonly Func<NestedShell?> _shell;
    private readonly Action<Action> _post;
    private readonly Action<string?, string, string> _launch;
    private readonly Action _openManager;
    private readonly Action _openSettings;
    private readonly Action<string> _disconnect;
    private readonly Action _quit;

    public PanelCommands(
        Func<NestedShell?> shell,
        Action<Action> post,
        Action<string?, string, string> launch,
        Action openManager,
        Action openSettings,
        Action<string> disconnect,
        Action quit)
    {
        _shell = shell;
        _post = post;
        _launch = launch;
        _openManager = openManager;
        _openSettings = openSettings;
        _disconnect = disconnect;
        _quit = quit;
    }

    public void FocusWindow(long id) => WithWindow(id, static (shell, window) => shell.ActivateWindow(window));

    public void MinimizeWindow(long id) => WithWindow(id, static (shell, window) => shell.SetMinimized(window, true));

    public void CloseWindow(long id) => WithWindow(id, static (shell, window) => shell.CloseWindow(window));

    public void ShowWindowMenu(long id) => WithWindow(id, static (shell, window) =>
        shell.ShowWindowMenu(window, window.FrameBox.X, window.ClientBox.Y));

    public void SwitchWorkspace(int index) => _post(() => _shell()?.SwitchWorkspace(index));

    public void MoveToWorkspace(long id, int index) => WithWindow(id, (shell, window) => shell.MoveToWorkspace(window, index));

    public void Launch(string? session, string label, string command) => _launch(session, label, command);

    public void OpenPlace(string session, string path) => _launch(session, $"Places: {path}", $"xdg-open {path}");

    public void OpenManager() => _openManager();

    public void OpenSettings() => _openSettings();

    public void Disconnect(string session) => _disconnect(session);

    public void Quit() => _quit();

    private void WithWindow(long id, Action<NestedShell, ManagedWindow> action) => _post(() =>
    {
        if (_shell() is { } shell && shell.WindowById(id) is { } window)
        {
            action(shell, window);
        }
    });
}
