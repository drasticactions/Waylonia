using Wayland.Server;

namespace Waylonia.Sessions;

internal interface IChannelOwner
{
    string Name { get; }

    SshSession? Session { get; }

    int Attached { get; }

    bool Carries(string interfaceName);

    bool ExclusiveDmabuf(WlGlobal global);

    bool SeesDmabuf(WlGlobal global);
}
