using Waylonia.Shell;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class ShellKeyLinesTests
{
    [Fact]
    public void Keys_render_one_per_line_and_parse_back()
    {
        var keys = new List<ShellKey>
        {
            new("close", "Super+q"),
            new("minimize", string.Empty),
            new("tile-to-side-w", "Super+Left"),
        };

        var text = ShellKeyLines.Render(keys);
        Assert.Equal("close = Super+q\nminimize = \ntile-to-side-w = Super+Left", text);

        var parsed = ShellKeyLines.Parse(text, out var problem);
        Assert.Null(problem);
        Assert.Equal(keys, parsed);
    }

    [Fact]
    public void Nothing_renders_as_nothing_and_parses_to_no_keys()
    {
        Assert.Equal(string.Empty, ShellKeyLines.Render(null));
        Assert.Equal(string.Empty, ShellKeyLines.Render([]));
        Assert.Empty(ShellKeyLines.Parse(null, out var problem)!);
        Assert.Null(problem);
        Assert.Empty(ShellKeyLines.Parse("  \n\n", out problem)!);
        Assert.Null(problem);
    }

    [Fact]
    public void Quotes_and_spacing_are_forgiven()
    {
        var parsed = ShellKeyLines.Parse("\"close\"=\"Alt+F4\"\n  minimize   =   Alt+F9  \n", out var problem);
        Assert.Null(problem);
        Assert.Equal([new ShellKey("close", "Alt+F4"), new ShellKey("minimize", "Alt+F9")], parsed);
    }

    [Fact]
    public void A_line_without_an_equals_sign_is_a_problem()
    {
        Assert.Null(ShellKeyLines.Parse("close Alt+F4", out var problem));
        Assert.Equal("Write one key per line as NAME = CHORD. 'close Alt+F4' has no '='.", problem);
    }

    [Fact]
    public void An_unknown_name_is_a_problem_naming_a_few_known_ones()
    {
        Assert.Null(ShellKeyLines.Parse("launch-terminal = Ctrl+Alt+t", out var problem));
        Assert.Equal(
            "'launch-terminal' is not a key. The keys are switch-windows, switch-windows-backward, switch-windows-all, switch-windows-all-backward and so on.",
            problem);
    }

    [Fact]
    public void A_bad_chord_carries_the_key_table_problem()
    {
        Assert.Null(ShellKeyLines.Parse("close = Hyper+F4", out var problem));
        Assert.Equal("close: 'Hyper+F4' has no modifier named 'Hyper'; the modifiers are shift, ctrl, alt and super.", problem);

        Assert.Null(ShellKeyLines.Parse("close = Alt+Nope", out problem));
        Assert.Equal("close: 'Alt+Nope' names no key.", problem);
    }

    [Fact]
    public void An_empty_chord_means_disabled()
    {
        var parsed = ShellKeyLines.Parse("close =\nminimize = \"\"", out var problem);
        Assert.Null(problem);
        Assert.Equal([new ShellKey("close", string.Empty), new ShellKey("minimize", string.Empty)], parsed);
    }

    [Fact]
    public void A_name_listed_twice_is_a_problem()
    {
        Assert.Null(ShellKeyLines.Parse("close = Alt+F4\nclose = Super+q", out var problem));
        Assert.Equal("'close' is listed twice. Keep one line per key.", problem);
    }
}
