using Basin.Avalonia;
using Basin.Hosted;
using Basin.Shell.Nested;
using Waylonia.Agent;

namespace Waylonia;

internal sealed class VisibleAgentCompositor : IAgentCompositor
{
    private readonly BasinOutputView _view;
    private readonly BasinCompositorHost _host;
    private readonly NestedShell _shell;

    public VisibleAgentCompositor(BasinOutputView view, BasinCompositorHost host, NestedShell shell)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(shell);
        _view = view;
        _host = host;
        _shell = shell;
        _host.Composited += _ => Damaged?.Invoke();
    }

    public bool Headless => false;

    public event Action? Damaged;

    public Task<T> RunAsync<T>(Func<NestedShell, T> work, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _view.Post(() =>
        {
            try
            {
                done.TrySetResult(work(_shell));
            }
            catch (Exception failure)
            {
                done.TrySetException(failure);
            }
        });
        return done.Task.WaitAsync(cancel);
    }

    public Task<bool> NextFrameAsync(CancellationToken cancel)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _view.Post(() =>
        {
            void OnComposited(long _)
            {
                _host.Composited -= OnComposited;
                done.TrySetResult(true);
            }

            _host.Composited += OnComposited;
        });
        _view.RequestFrame();
        return done.Task.WaitAsync(cancel);
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _view.Post(action);
    }
}
