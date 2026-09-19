using Basin.Diagnostics;
using Basin.Shell.Nested;
using Xunit;

namespace Waylonia.Tests.Shell;

[Collection(LogCaptureCollection.Name)]
public sealed class KeyTableTests
{
    [Fact]
    public void A_host_hotkey_on_the_same_chord_wins_and_is_reported_once()
    {
        var hotkeys = new[] { Hotkey.Parse("alt+F4", "foot", BasinLogger.None)! };
        using var capture = new LogCapture();
        var table = KeyTable.Build([], Hotkey.Reserved(hotkeys));

        Assert.Equal(
            ["Warn: shell key close uses Alt+F4, which the host already binds, keeping the host's"],
            capture.Lines.Where(line => line.StartsWith("Warn", StringComparison.Ordinal)));
        Assert.Null(table.Match(ShellModifiers.Alt, 62));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "close");
        Assert.Equal("minimize", table.Match(ShellModifiers.Alt, 67));
    }

    [Fact]
    public void A_hotkey_naming_no_key_reserves_nothing()
    {
        var hotkeys = new[] { Hotkey.Parse("alt+Bogus", "foot", BasinLogger.None)! };

        Assert.Empty(Hotkey.Reserved(hotkeys));
        Assert.Equal("close", KeyTable.Build([], Hotkey.Reserved(hotkeys)).Match(ShellModifiers.Alt, 62));
    }
}
