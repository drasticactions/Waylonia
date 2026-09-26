using Tmds.DBus.Protocol;

namespace Waylonia.Accessibility;

/// <summary>
/// A connection to the accessibility bus of one session bus, found through <c>org.a11y.Bus</c>, that reports every
/// AT-SPI event signal on it.
/// </summary>
public sealed class A11yBus : IAsyncDisposable
{
    internal const string LauncherName = "org.a11y.Bus";
    internal const string LauncherPath = "/org/a11y/bus";
    internal const string RegistryName = "org.a11y.atspi.Registry";
    internal const string RegistryPath = "/org/a11y/atspi/registry";
    internal const string RootPath = "/org/a11y/atspi/accessible/root";
    internal const string CachePath = "/org/a11y/atspi/cache";

    private static readonly string[] Events = ["object:", "window:", "focus:", "document:"];

    private readonly DBusConnection _connection;
    private IDisposable? _events;
    private int _disposed;

    private A11yBus(DBusConnection connection, string address)
    {
        _connection = connection;
        Address = address;
    }

    internal DBusConnection Connection => _connection;

    /// <summary>
    /// The address of the accessibility bus.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Raised on a D-Bus thread whenever an <c>org.a11y.atspi.Event.*</c> signal, or a <c>Cache</c> add or remove,
    /// arrives from any application.
    /// </summary>
    public event Action? EventReceived;

    /// <summary>
    /// Turns accessibility on for a session bus: sets <c>org.a11y.Status.IsEnabled</c> and
    /// <c>ScreenReaderEnabled</c>, which Firefox and Chromium wait for before they build a tree. The call starts
    /// <c>at-spi-bus-launcher</c> through D-Bus activation when it is not running.
    /// </summary>
    /// <param name="sessionBusAddress">The session bus address.</param>
    /// <param name="cancel">Cancels the wait.</param>
    /// <returns>A task that completes once both properties are set.</returns>
    public static async Task EnableAsync(string sessionBusAddress, CancellationToken cancel)
    {
        using var session = await ConnectSessionAsync(sessionBusAddress, cancel).ConfigureAwait(false);
        var status = new DBus.Status(session, LauncherName, LauncherPath);
        try
        {
            await status.SetIsEnabledAsync(true).WaitAsync(cancel).ConfigureAwait(false);
            await status.SetScreenReaderEnabledAsync(true).WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException error)
        {
            throw LauncherProblem(error);
        }
    }

    /// <summary>
    /// Asks the session bus for the accessibility bus address, connects to it, and listens for event signals.
    /// </summary>
    /// <param name="sessionBusAddress">The session bus address.</param>
    /// <param name="cancel">Cancels the connection.</param>
    /// <returns>The connected bus.</returns>
    public static async Task<A11yBus> ConnectAsync(string sessionBusAddress, CancellationToken cancel)
    {
        string address;
        using (var session = await ConnectSessionAsync(sessionBusAddress, cancel).ConfigureAwait(false))
        {
            try
            {
                address = await new DBus.Bus(session, LauncherName, LauncherPath)
                    .GetAddressAsync().WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (DBusErrorReplyException error)
            {
                throw LauncherProblem(error);
            }
        }

        if (string.IsNullOrEmpty(address))
        {
            throw new A11yException("at-spi-bus-launcher answered with no accessibility bus address");
        }

        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusConnectFailedException or DBusConnectionException)
        {
            connection.Dispose();
            throw new A11yException($"the accessibility bus at {address} could not be reached: {error.Message}");
        }

        var bus = new A11yBus(connection, address);
        try
        {
            await bus.ListenAsync(cancel).ConfigureAwait(false);
        }
        catch
        {
            await bus.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return bus;
    }

    /// <summary>
    /// Waits until <c>org.a11y.atspi.Registry</c> owns its name on the accessibility bus of a session bus. A host
    /// that starts <c>at-spi2-registryd</c> itself, because activation cannot reach it, waits here before it
    /// connects.
    /// </summary>
    /// <param name="sessionBusAddress">The session bus address.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancel">Cancels the wait.</param>
    /// <returns>True once the registry owns its name, false at the timeout.</returns>
    public static async Task<bool> WaitForRegistryAsync(string sessionBusAddress, TimeSpan timeout, CancellationToken cancel)
    {
        string address;
        using (var session = await ConnectSessionAsync(sessionBusAddress, cancel).ConfigureAwait(false))
        {
            try
            {
                address = await new DBus.Bus(session, LauncherName, LauncherPath)
                    .GetAddressAsync().WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (DBusErrorReplyException error)
            {
                throw LauncherProblem(error);
            }
        }

        using var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DBusConnectFailedException or DBusConnectionException)
        {
            throw new A11yException($"the accessibility bus at {address} could not be reached: {error.Message}");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await connection.ListServicesAsync().ConfigureAwait(false)).Contains(RegistryName))
            {
                return true;
            }

            await Task.Delay(50, cancel).ConfigureAwait(false);
        }

        return false;
    }

    private async Task ListenAsync(CancellationToken cancel)
    {
        _events = await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal },
            static (Message message, object? _) => message.InterfaceAsString ?? string.Empty,
            (Notification<string> notification) =>
            {
                if (notification.HasValue && IsEvent(notification.Value))
                {
                    Raise();
                }
            },
            emitOnCapturedContext: false,
            ObserverFlags.None).AsTask().WaitAsync(cancel).ConfigureAwait(false);

        var registry = new DBus.Registry(_connection, RegistryName, RegistryPath);
        foreach (var name in Events)
        {
            try
            {
                await registry.RegisterEventAsync(name, [], string.Empty)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancel).ConfigureAwait(false);
            }
            catch (Exception error) when (error is DBusErrorReplyException or TimeoutException)
            {
                break;
            }
        }
    }

    internal static bool IsEvent(string @interface) =>
        @interface.StartsWith("org.a11y.atspi.Event.", StringComparison.Ordinal) ||
        @interface == "org.a11y.atspi.Cache";

    private void Raise()
    {
        try
        {
            EventReceived?.Invoke();
        }
        catch (Exception)
        {
        }
    }

    private static async Task<DBusConnection> ConnectSessionAsync(string address, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new A11yException("no session bus address was given, so the accessibility bus cannot be found");
        }

        var session = new DBusConnection(address);
        try
        {
            await session.ConnectAsync().AsTask().WaitAsync(cancel).ConfigureAwait(false);
            return session;
        }
        catch (Exception error) when (error is DBusConnectFailedException or DBusConnectionException)
        {
            session.Dispose();
            throw new A11yException($"the session bus at {address} could not be reached: {error.Message}");
        }
    }

    private static A11yException LauncherProblem(DBusErrorReplyException error) =>
        error.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
            or "org.freedesktop.DBus.Error.Spawn.ExecFailed" or "org.freedesktop.DBus.Error.Spawn.ChildExited"
            ? new A11yException(
                $"the session bus has no org.a11y.Bus, so there is no accessibility bus; at-spi2-core provides it ({error.ErrorMessage})")
            : new A11yException($"org.a11y.Bus refused the request: {error.ErrorMessage}");

    /// <summary>
    /// Stops listening and closes the accessibility bus connection.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _events?.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
