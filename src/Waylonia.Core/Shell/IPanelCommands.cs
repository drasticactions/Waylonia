namespace Waylonia.Shell;

internal interface IPanelCommands
{
    void FocusWindow(long id);

    void MinimizeWindow(long id);

    void CloseWindow(long id);

    void ShowWindowMenu(long id);

    void SwitchWorkspace(int index);

    void MoveToWorkspace(long id, int index);

    void Launch(string? session, string label, string command);

    void OpenPlace(string session, string path);

    void OpenManager();

    void OpenSettings();

    void Disconnect(string session);

    void Quit();
}
