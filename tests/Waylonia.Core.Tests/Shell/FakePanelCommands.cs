using Waylonia.Shell;

namespace Waylonia.Tests.Shell;

internal sealed class FakePanelCommands : IPanelCommands
{
    public List<string> Calls { get; } = [];

    public void FocusWindow(long id) => Calls.Add($"FocusWindow {id}");

    public void MinimizeWindow(long id) => Calls.Add($"MinimizeWindow {id}");

    public void CloseWindow(long id) => Calls.Add($"CloseWindow {id}");

    public void ShowWindowMenu(long id) => Calls.Add($"ShowWindowMenu {id}");

    public void SwitchWorkspace(int index) => Calls.Add($"SwitchWorkspace {index}");

    public void MoveToWorkspace(long id, int index) => Calls.Add($"MoveToWorkspace {id} {index}");

    public void Launch(string? session, string label, string command) => Calls.Add($"Launch {session ?? "local"} {label} {command}");

    public void OpenPlace(string session, string path) => Calls.Add($"OpenPlace {session} {path}");

    public void OpenManager() => Calls.Add("OpenManager");

    public void OpenSettings() => Calls.Add("OpenSettings");

    public void Disconnect(string session) => Calls.Add($"Disconnect {session}");

    public void Quit() => Calls.Add("Quit");

    public void ToggleSoftKeyboard() => Calls.Add("ToggleSoftKeyboard");
}
