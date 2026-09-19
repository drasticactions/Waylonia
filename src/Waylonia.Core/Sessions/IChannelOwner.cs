using Basin.Transport.Waypipe;
using Wayland.Server;

namespace Waylonia.Sessions;

internal interface IChannelOwner
{
    string Name { get; }

    WaypipeGlobals Globals { get; }

    bool OwnsDmabuf(WlGlobal global);
}
