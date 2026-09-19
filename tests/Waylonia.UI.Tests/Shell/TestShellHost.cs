using Avalonia.Controls;
using Waylonia.Shell;

namespace Waylonia.Tests.Shell;

internal sealed class TestShellHost : IShellHost
{
    public TestShellHost(Window window, ShellView? view = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        Window = window;
        View = view ?? new ShellView();
        window.Content = View;
        window.Deactivated += (_, _) => DeactivatedOnHost?.Invoke();
        window.Activated += (_, _) => ActivatedOnHost?.Invoke();
    }

    public Window Window { get; }

    public ShellView View { get; }

    public TopLevel? TopLevel => Window;

    public bool CanFullScreen => false;

    public string Title { get; private set; } = string.Empty;

    public int Presented { get; private set; }

    public event Action? ActivatedOnHost;

    public event Action? DeactivatedOnHost;

    public void ToggleFullScreen()
    {
    }

    public void SetTitle(string title) => Title = title;

    public void Present() => Presented++;

    public Task CloseAsync()
    {
        Window.Close();
        return Task.CompletedTask;
    }
}
