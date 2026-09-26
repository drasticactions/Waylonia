using System.Runtime.Versioning;
using Basin.Shell.Nested;
using Waylonia.Accessibility;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal sealed class AgentAccessibility : IDisposable
{
    private static readonly string[] Launchers =
    [
        "/usr/share/dbus-1/services/org.a11y.Bus.service",
        "/usr/local/share/dbus-1/services/org.a11y.Bus.service",
    ];

    private static readonly string[] Registries =
    [
        "/usr/lib/at-spi2-registryd",
        "/usr/libexec/at-spi2-registryd",
        "/usr/lib/at-spi2-core/at-spi2-registryd",
        "/usr/lib/x86_64-linux-gnu/at-spi2-registryd",
    ];

    private readonly Task<A11yService> _service;
    private readonly CancellationTokenSource _stop = new();
    private readonly IReadOnlyDictionary<string, string> _environment;
    private System.Diagnostics.Process? _registry;
    private A11yBus? _bus;
    private string? _failure;
    private bool _disposed;

    public AgentAccessibility(string busAddress, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(busAddress);
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _service = Task.Run(() => ConnectAsync(busAddress, _stop.Token));
    }

    public string Status => _service.IsCompletedSuccessfully
        ? "up"
        : _failure is { } failure ? $"off: {failure}" : "starting";

    public event Action? EventReceived;

    public static string? Unavailable() =>
        Launchers.Any(File.Exists)
            ? null
            : "at-spi2-core is not installed (no org.a11y.Bus.service), so the agent's applications have no accessibility bus";

    public async Task<A11yService> ServiceAsync(CancellationToken cancel)
    {
        try
        {
            return await _service.WaitAsync(TimeSpan.FromSeconds(20), cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new A11yException("the accessibility bus did not come up within 20 s");
        }
    }

    public async Task<A11yWindow?> FindAsync(A11yWindowTarget target, CancellationToken cancel)
    {
        var service = await ServiceAsync(cancel).ConfigureAwait(false);
        return await service.FindWindowAsync(target, cancel).ConfigureAwait(false);
    }

    public static A11yWindowTarget TargetOf(ManagedWindow window, int pid)
    {
        ArgumentNullException.ThrowIfNull(window);
        var geometry = window.Content.Geometry;
        var client = window.ClientBox;
        var surface = window.Content.Surface?.Current;
        var surfaceWidth = surface is { Width: > 0 } ? surface.Width : client.Width;
        var surfaceHeight = surface is { Height: > 0 } ? surface.Height : client.Height;
        return new A11yWindowTarget(
            pid, window.Content.Title, client.Width, client.Height, surfaceWidth, surfaceHeight, geometry.X, geometry.Y);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        if (_bus is { } bus)
        {
            try
            {
                bus.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException failure)
            {
                Log.Debug($"the accessibility bus did not close cleanly: {failure.InnerException?.Message}");
            }
        }

        try
        {
            if (_registry is { HasExited: false } registry)
            {
                registry.Kill();
                registry.WaitForExit(1000);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Debug($"at-spi2-registryd did not end cleanly: {failure.Message}");
        }

        _registry?.Dispose();
        _stop.Dispose();
    }

    private void StartRegistry()
    {
        if (_registry is not null || Registries.FirstOrDefault(File.Exists) is not { } path)
        {
            return;
        }

        var start = new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (var (name, value) in _environment)
        {
            start.Environment[name] = value;
        }

        try
        {
            _registry = System.Diagnostics.Process.Start(start);
            if (_registry is { } registry)
            {
                registry.OutputDataReceived += static (_, _) => { };
                registry.ErrorDataReceived += static (_, _) => { };
                registry.BeginOutputReadLine();
                registry.BeginErrorReadLine();
            }
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Debug($"{path} did not start: {failure.Message}");
        }
    }

    private async Task<A11yService> ConnectAsync(string address, CancellationToken cancel)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 20 && !cancel.IsCancellationRequested; attempt++)
        {
            try
            {
                await A11yBus.EnableAsync(address, cancel).ConfigureAwait(false);
                StartRegistry();
                if (!await A11yBus.WaitForRegistryAsync(address, TimeSpan.FromSeconds(10), cancel).ConfigureAwait(false))
                {
                    Log.Warn($"at-spi2-registryd did not take its name on the agent's accessibility bus within 10 s");
                }

                var bus = await A11yBus.ConnectAsync(address, cancel).ConfigureAwait(false);
                bus.EventReceived += () => EventReceived?.Invoke();
                _bus = bus;
                Log.Debug($"the agent's accessibility bus is up");
                return new A11yService(bus);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                last = failure;
                await Task.Delay(250, cancel).ConfigureAwait(false);
            }
        }

        _failure = last?.Message ?? "the accessibility bus did not answer";
        Log.Warn($"the agent's accessibility bus did not come up: {_failure}");
        throw new A11yException(_failure);
    }
}
