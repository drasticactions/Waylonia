using Waylonia;
using Xunit;

using Basin.Diagnostics;
using Basin.Shell.Nested;

namespace Waylonia.Tests;

public sealed class HotkeyTests
{
    private static Hotkey? Parse(string chord, string? command = "foot") =>
        Hotkey.Parse(chord, command, BasinLogger.None);

    [Theory]
    [InlineData("ctrl+t", nameof(ShellModifiers.Ctrl))]
    [InlineData("control+t", nameof(ShellModifiers.Ctrl))]
    [InlineData("alt+t", nameof(ShellModifiers.Alt))]
    [InlineData("option+t", nameof(ShellModifiers.Alt))]
    [InlineData("shift+t", nameof(ShellModifiers.Shift))]
    [InlineData("super+t", nameof(ShellModifiers.Super))]
    [InlineData("cmd+t", nameof(ShellModifiers.Super))]
    [InlineData("command+t", nameof(ShellModifiers.Super))]
    [InlineData("win+t", nameof(ShellModifiers.Super))]
    [InlineData("logo+t", nameof(ShellModifiers.Super))]
    public void Every_modifier_spelling_reaches_the_same_flag(string chord, string expected)
    {
        var hotkey = Assert.IsType<Hotkey>(Parse(chord));

        Assert.Equal(Enum.Parse<ShellModifiers>(expected), hotkey.Modifiers);
        Assert.Equal("t", hotkey.Key);
    }

    [Fact]
    public void Modifiers_accumulate_and_the_last_token_is_the_key()
    {
        var hotkey = Assert.IsType<Hotkey>(Parse("Ctrl + Alt + Shift + Super + F5"));

        Assert.Equal(
            ShellModifiers.Ctrl | ShellModifiers.Alt | ShellModifiers.Shift | ShellModifiers.Super,
            hotkey.Modifiers);
        Assert.Equal("f5", hotkey.Key);
    }

    [Fact]
    public void A_bare_key_carries_no_modifier()
    {
        var hotkey = Assert.IsType<Hotkey>(Parse("f12"));

        Assert.Equal(ShellModifiers.None, hotkey.Modifiers);
        Assert.Equal("f12", hotkey.Key);
    }

    [Fact]
    public void An_unknown_modifier_drops_the_whole_chord()
    {
        Assert.Null(Parse("hyper+t"));
    }

    [Fact]
    public void A_chord_that_names_no_key_is_dropped()
    {
        Assert.Null(Parse("+"));
    }

    [Fact]
    public void A_chord_without_a_command_is_dropped()
    {
        Assert.Null(Parse("ctrl+alt+t", command: null));
    }

    [Fact]
    public void The_chord_is_kept_as_it_was_written()
    {
        var hotkey = Assert.IsType<Hotkey>(Parse("Ctrl+Alt+T"));

        Assert.Equal("Ctrl+Alt+T", hotkey.Chord);
    }
}
