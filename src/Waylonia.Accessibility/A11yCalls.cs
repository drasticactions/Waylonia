using Tmds.DBus.Protocol;

namespace Waylonia.Accessibility;

internal static class A11yCalls
{
    public static async Task<T> Run<T>(
        Task<T> call,
        A11yNodeRef node,
        string what,
        CancellationToken cancel,
        bool unsupportedMeansGone = false)
    {
        try
        {
            return await call.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException error)
        {
            throw Translate(error, node, what, unsupportedMeansGone);
        }
        catch (DBusConnectionClosedException)
        {
            throw Closed();
        }
        catch (DBusConnectionException)
        {
            throw Closed();
        }
    }

    public static async Task Run(Task call, A11yNodeRef node, string what, CancellationToken cancel)
    {
        try
        {
            await call.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException error)
        {
            throw Translate(error, node, what);
        }
        catch (DBusConnectionClosedException)
        {
            throw Closed();
        }
        catch (DBusConnectionException)
        {
            throw Closed();
        }
    }

    public static async Task<T?> TryRun<T>(Task<T> call, CancellationToken cancel)
    {
        try
        {
            return await call.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return default;
        }
        catch (DBusConnectionClosedException)
        {
            throw Closed();
        }
        catch (DBusConnectionException)
        {
            throw Closed();
        }
    }

    public static A11yException Closed() => new("the accessibility bus connection is closed");

    public static A11yException Translate(
        DBusErrorReplyException error,
        A11yNodeRef node,
        string what,
        bool unsupportedMeansGone = false)
    {
        var name = error.ErrorName ?? string.Empty;
        var message = error.ErrorMessage ?? string.Empty;
        return name switch
        {
            "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" =>
                new A11yException($"the application behind node {node.Id} is gone"),
            "org.freedesktop.DBus.Error.UnknownObject" =>
                new A11yException($"node {node.Id} no longer exists"),
            "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface"
                or "org.freedesktop.DBus.Error.UnknownProperty" when unsupportedMeansGone || LooksGone(message) =>
                new A11yException($"node {node.Id} no longer exists"),
            "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface"
                or "org.freedesktop.DBus.Error.UnknownProperty" =>
                new A11yException($"node {node.Id} does not support {what}"),
            "org.freedesktop.DBus.Error.NotSupported" =>
                new A11yException($"node {node.Id} does not support {what}"),
            "org.freedesktop.DBus.Error.NoReply" or "org.freedesktop.DBus.Error.Timeout"
                or "org.freedesktop.DBus.Error.TimedOut" =>
                new A11yException($"the application behind node {node.Id} did not answer {what}"),
            _ => new A11yException(
                $"node {node.Id} failed {what}: {(message.Length > 0 ? message : name)}"),
        };
    }

    private static bool LooksGone(string message) =>
        message.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Unknown object", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
}
