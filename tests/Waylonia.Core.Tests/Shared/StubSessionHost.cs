using Basin.Hosted;
using Wayland.Server;
using Waylonia.Audio;
using Waylonia.Sessions;
using Waylonia.Tests.Ssh;

namespace Waylonia.Tests;

internal sealed class StubSessionHost : ISessionHost
{
    public BasinCompositorHost Compositor => throw new InvalidOperationException("the stub host has no compositor");

    public HostSettings Settings { get; init; } = new();

    public FakeSshLinkFactory Links { get; } = new();

    public FakePrompter Prompter { get; } = new();

    ISshLinkFactory ISessionHost.Links => Links;

    public Dictionary<DestinationKind, FakeSshLinkFactory> Targets { get; } = [];

    public FakeSshLinkFactory? WslLinks
    {
        get => Targets.GetValueOrDefault(DestinationKinds.Wsl);
        init
        {
            if (value is not null)
            {
                Targets[DestinationKinds.Wsl] = value;
            }
        }
    }

    ITargetLinkFactory? ISessionHost.LinksFor(DestinationKind kind) => Targets.GetValueOrDefault(kind);

    ISshPrompter ISessionHost.Prompter => Prompter;

    public List<Action> Posted { get; } = [];

    public int Attached { get; private set; }

    public AudioMixer Audio { get; } = new(_ => null);

    public bool ShuttingDown { get; set; }

    public List<string> StatusLines { get; } = [];

    public void Post(Action action) => Posted.Add(action);

    public void Attach(IChannelOwner owner, WlClient client) => Attached++;

    public List<(int Fd, DirectOwner Owner)> Adopted { get; } = [];

    public int Released { get; private set; }

    public string? XDisplay { get; set; }

    public IDisposable AdoptDisplay(int listenFd, DirectOwner owner)
    {
        Adopted.Add((listenFd, owner));
        return new Release(() => Released++);
    }

    private sealed class Release(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    public void Status(string text) => StatusLines.Add(text);

    public static SessionSettings For(string name, bool desktop = false, bool adHoc = false) => new(
        name,
        $"user@{name}",
        null,
        [],
        Basin.Transport.Waypipe.WaypipeCompression.Lz4,
        false,
        false,
        "f32",
        null,
        null,
        null,
        null,
        null,
        [],
        desktop ? DesktopRecipes.Find("sway") : null,
        [],
        null,
        adHoc);
}
