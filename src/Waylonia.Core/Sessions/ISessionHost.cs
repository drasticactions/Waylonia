using Basin.Hosted;
using Wayland.Server;
using Waylonia.Audio;

using Basin.Freedesktop;

namespace Waylonia.Sessions;

internal interface ISessionHost
{
    BasinCompositorHost Compositor { get; }

    HostSettings Settings { get; }

    ISshLinkFactory Links { get; }

    ITargetLinkFactory? LinksFor(DestinationKind kind);

    ISshPrompter Prompter { get; }

    IconCache? Icons => null;

    string? ThemeIconFor(DesktopEntry entry) => null;

    string? ThemeIconFor(DesktopMainCategory category) => null;

    AudioMixer Audio { get; }

    bool ShuttingDown { get; }

    string? XDisplay => null;

    void Post(Action action);

    void Attach(IChannelOwner owner, WlClient client);

    IDisposable AdoptDisplay(int listenFd, DirectOwner owner);

    void Status(string text);
}
