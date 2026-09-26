using Tmds.DBus.Protocol;

namespace Waylonia.Accessibility;

internal static class BusDaemon
{
    private const string Name = "org.freedesktop.DBus";
    private const string ObjectPath = "/org/freedesktop/DBus";

    public static Task<uint> ProcessIdAsync(DBusConnection connection, string busName)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Name,
            path: ObjectPath,
            @interface: Name,
            signature: "s",
            member: "GetConnectionUnixProcessID");
        writer.WriteString(busName);
        var message = writer.CreateMessage();
        return connection.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);
    }
}
