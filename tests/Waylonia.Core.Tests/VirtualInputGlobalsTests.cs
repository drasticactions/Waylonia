using Xunit;

namespace Waylonia.Tests;

public sealed class VirtualInputGlobalsTests
{
    private static IReadOnlyList<string> Advertised(bool keep)
    {
        using var harness = new CompositorHarness(services: (_, services) => VirtualInputGlobals.Apply(services, keep));
        return harness.Client.Globals.Select(static global => global.Interface).ToArray();
    }

    [Fact]
    public void A_run_offers_no_virtual_input_by_default()
    {
        var globals = Advertised(keep: false);
        Assert.Contains("wl_seat", globals);
        Assert.DoesNotContain(VirtualInputGlobals.Keyboard, globals);
        Assert.DoesNotContain(VirtualInputGlobals.Pointer, globals);
    }

    [Fact]
    public void Virtual_input_keeps_both_globals()
    {
        var globals = Advertised(keep: true);
        Assert.Contains(VirtualInputGlobals.Keyboard, globals);
        Assert.Contains(VirtualInputGlobals.Pointer, globals);
    }
}
