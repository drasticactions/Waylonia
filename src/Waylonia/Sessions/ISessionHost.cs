using Basin.Avalonia;
using Wayland.Server;
using Waylonia.Audio;

using Basin.Freedesktop;

namespace Waylonia.Sessions;

internal interface ISessionHost
{
    BasinCompositorHost Compositor { get; }

    HostSettings Settings { get; }

    ISshLinkFactory Links { get; }

    ISshPrompter Prompter { get; }

    IconCache? Icons => null;

    string? ThemeIconFor(DesktopEntry entry) => null;

    string? ThemeIconFor(DesktopMainCategory category) => null;

    AudioMixer Audio { get; }

    bool ShuttingDown { get; }

    void Post(Action action);

    void Attach(WaypipeAcceptor owner, WlClient client);

    void Status(string text);
}
