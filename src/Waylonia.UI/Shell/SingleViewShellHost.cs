using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Waylonia.Shell;

internal sealed class SingleViewShellHost : IShellHost
{
    public SingleViewShellHost(ShellView view, IActivatableLifetime? activatable)
    {
        ArgumentNullException.ThrowIfNull(view);
        View = view;
        if (activatable is null)
        {
            return;
        }

        activatable.Activated += (_, e) =>
        {
            if (e.Kind == ActivationKind.Background)
            {
                View.NotifyActivated(true);
                ActivatedOnHost?.Invoke();
            }
        };
        activatable.Deactivated += (_, e) =>
        {
            if (e.Kind == ActivationKind.Background)
            {
                View.NotifyActivated(false);
                DeactivatedOnHost?.Invoke();
            }
        };
    }

    public ShellView View { get; }

    public TopLevel? TopLevel => TopLevel.GetTopLevel(View);

    public bool CanFullScreen => false;

    public event Action? ActivatedOnHost;

    public event Action? DeactivatedOnHost;

    public void ToggleFullScreen()
    {
    }

    public void SetTitle(string title)
    {
    }

    public void Present() => View.FocusShell();

    public Task CloseAsync() => View.Toplevel?.ShutdownAsync() ?? Task.CompletedTask;
}
