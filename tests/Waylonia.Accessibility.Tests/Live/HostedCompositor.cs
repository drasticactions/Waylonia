using System.Collections.Concurrent;
using Basin.Hosted;
using Basin.Shell.Xdg;

namespace Waylonia.Accessibility.Tests.Live;

internal sealed class HostedCompositor : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action<BasinCompositorHost>> _work = new();
    private readonly List<XdgToplevelWindow> _toplevels = [];
    private readonly TaskCompletionSource<string> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stop;

    public HostedCompositor()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "a11y-tests compositor" };
        _thread.Start();
        SocketPath = _ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public string SocketPath { get; }

    private void Run()
    {
        BasinCompositorHost host;
        try
        {
            host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "a11y-tests" });
            host.Shell.NewToplevel += window =>
            {
                _toplevels.Add(window);
                window.Destroyed += () => _toplevels.Remove(window);
            };
            var socket = Path.IsPathRooted(host.Socket)
                ? host.Socket
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp", host.Socket);
            _ready.SetResult(socket);
        }
        catch (Exception error)
        {
            _ready.SetException(error);
            return;
        }

        var nextFrame = Environment.TickCount64;
        while (!_stop)
        {
            while (_work.TryDequeue(out var action))
            {
                action(host);
            }

            host.Loop.Dispatch(5);
            host.Display.FlushClients();
            var now = Environment.TickCount64;
            if (now >= nextFrame)
            {
                nextFrame = now + 16;
                if (host.EnterFrame(TimeSpan.FromMilliseconds(now)))
                {
                    host.ExitFrame();
                }

                foreach (var window in _toplevels.ToArray())
                {
                    if (!window.Surface.IsDestroyed)
                    {
                        window.Surface.SendFrameDone((uint)now);
                    }
                }

                host.Display.FlushClients();
            }
        }

        host.Dispose();
    }

    public Task<T> InvokeAsync<T>(Func<BasinCompositorHost, T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(host =>
        {
            try
            {
                done.SetResult(action(host));
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });
        return done.Task;
    }

    public Task<IReadOnlyList<A11yWindowTarget>> WindowsAsync() => InvokeAsync<IReadOnlyList<A11yWindowTarget>>(_ =>
        _toplevels
            .Where(window => window.IsMapped)
            .Select(Target)
            .ToList());

    public async Task<A11yWindowTarget> WaitForWindowAsync(Func<A11yWindowTarget, bool> match, TimeSpan timeout, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var found = (await WindowsAsync().ConfigureAwait(false)).FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("no matching toplevel mapped on the test compositor");
            }

            await Task.Delay(100, cancel).ConfigureAwait(false);
        }
    }

    private static A11yWindowTarget Target(XdgToplevelWindow window)
    {
        var pid = window.Surface.Resource.Client.TryGetCredentials(out var credentials) ? credentials.Pid : 0;
        var surfaceWidth = window.Surface.Current.Width;
        var surfaceHeight = window.Surface.Current.Height;
        var geometry = window.Xdg.WindowGeometry;
        if (geometry.Width <= 0 || geometry.Height <= 0)
        {
            geometry = new Basin.Box(0, 0, surfaceWidth, surfaceHeight);
        }

        return new A11yWindowTarget(
            pid,
            window.Title,
            geometry.Width,
            geometry.Height,
            surfaceWidth,
            surfaceHeight,
            geometry.X,
            geometry.Y);
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(10));
    }
}
