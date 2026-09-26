using Tmds.DBus.Protocol;

namespace Waylonia.Accessibility.Tests.Live;

internal sealed class BusAddressReader(DBusConnection session)
{
    public Task<string> GetAsync()
    {
        var writer = session.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.a11y.Bus", path: "/org/a11y/bus", @interface: "org.a11y.Bus", member: "GetAddress");
        return session.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadString(), null);
    }
}
