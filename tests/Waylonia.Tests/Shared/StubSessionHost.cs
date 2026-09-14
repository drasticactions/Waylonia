using Basin.Avalonia;
using Wayland.Server;
using Waylonia.Audio;
using Waylonia.Sessions;

namespace Waylonia.Tests;

internal sealed class StubSessionHost : ISessionHost
{
    public BasinCompositorHost Compositor => throw new InvalidOperationException("the stub host has no compositor");

    public HostSettings Settings { get; init; } = new();

    public AudioMixer Audio { get; } = new(_ => null);

    public bool ShuttingDown => false;

    public List<string> StatusLines { get; } = [];

    public void Post(Action action) => action();

    public void Attach(WaypipeAcceptor owner, WlClient client)
    {
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
