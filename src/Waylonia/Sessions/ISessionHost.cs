using Basin.Avalonia;
using Wayland.Server;
using Waylonia.Audio;

namespace Waylonia.Sessions;

internal interface ISessionHost
{
    BasinCompositorHost Compositor { get; }

    HostSettings Settings { get; }

    AudioMixer Audio { get; }

    bool ShuttingDown { get; }

    void Post(Action action);

    void Attach(WaypipeAcceptor owner, WlClient client);

    void Status(string text);
}
