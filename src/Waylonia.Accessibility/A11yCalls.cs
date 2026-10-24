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
        catch (DBusConnectionClosedException error)
        {
            throw Closed(error);
        }
        catch (DBusConnectionException error)
        {
            throw Closed(error);
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
        catch (DBusConnectionClosedException error)
        {
            throw Closed(error);
        }
        catch (DBusConnectionException error)
        {
            throw Closed(error);
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
        catch (DBusConnectionClosedException error)
        {
            throw Closed(error);
        }
        catch (DBusConnectionException error)
        {
            throw Closed(error);
        }
    }

    public static A11yException Closed(Exception inner) =>
        new("The connection to the accessibility bus closed. The accessibility methods stay unavailable until the agent session restarts.", inner);

    public static A11yException Gone(A11yNodeRef node) =>
        new($"Node {node.Id} no longer exists. Read the tree again for the current node ids.");

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
                new A11yException($"The application behind node {node.Id} has exited. Launch it again and read its tree.", error),
            "org.freedesktop.DBus.Error.UnknownObject" =>
                new A11yException(Gone(node).Message, error),
            "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface"
                or "org.freedesktop.DBus.Error.UnknownProperty" when unsupportedMeansGone || LooksGone(message) =>
                new A11yException(Gone(node).Message, error),
            "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface"
                or "org.freedesktop.DBus.Error.UnknownProperty" or "org.freedesktop.DBus.Error.NotSupported" =>
                new A11yException($"Node {node.Id} does not support {what}.", error),
            "org.freedesktop.DBus.Error.NoReply" or "org.freedesktop.DBus.Error.Timeout"
                or "org.freedesktop.DBus.Error.TimedOut" =>
                new A11yException($"The application behind node {node.Id} did not answer a request for {what}. It may be busy, so try again.", error),
            _ => new A11yException(
                $"Node {node.Id} failed a request for {what}. The application said \"{(message.Length > 0 ? message : name)}\".", error),
        };
    }

    private static bool LooksGone(string message) =>
        message.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Unknown object", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
}
