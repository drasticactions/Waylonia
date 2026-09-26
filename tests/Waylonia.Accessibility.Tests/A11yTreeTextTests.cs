using Xunit;

namespace Waylonia.Accessibility.Tests;

public sealed class A11yTreeTextTests
{
    [Fact]
    public void A_line_holds_id_role_name_states_and_box()
    {
        var node = new A11yNodeInfo(
            ":1.42:/org/a11y/atspi/accessible/7",
            "push-button",
            "Yes",
            ["enabled", "showing"],
            new A11yBox(12, 340, 80, 32),
            null);
        Assert.Equal(
            "[:1.42:/org/a11y/atspi/accessible/7] push-button \"Yes\" {enabled,showing} @12,340 80x32",
            A11yTreeText.Line(node, 0));
    }

    [Fact]
    public void Each_depth_indents_two_spaces_and_an_unknown_box_is_left_out()
    {
        var node = new A11yNodeInfo(":1.3:/a/b", "label", "", [], null, "ignored");
        Assert.Equal("    [:1.3:/a/b] label \"\" {}", A11yTreeText.Line(node, 2));
    }

    [Fact]
    public void Quotes_backslashes_and_line_breaks_in_a_name_are_escaped()
    {
        Assert.Equal("\"say \\\"hi\\\"\\nC:\\\\x\"", A11yTreeText.Quote("say \"hi\"\nC:\\x"));
    }

    [Fact]
    public void The_cap_lines_name_the_limit()
    {
        Assert.Equal("(stopped at the node cap of 300; more nodes remain)", A11yTreeText.NodeCapLine(300));
        Assert.Equal(
            "(depth limit of 4 reached; the children of 1 node are not listed)",
            A11yTreeText.DepthCapLine(4, 1));
        Assert.Equal(
            "(depth limit of 4 reached; the children of 3 nodes are not listed)",
            A11yTreeText.DepthCapLine(4, 3));
    }

    [Fact]
    public void Truncation_keeps_the_limit_and_never_splits_a_surrogate_pair()
    {
        Assert.Equal("abc", A11yTreeText.Truncate("abc", 3));
        Assert.Equal("ab…", A11yTreeText.Truncate("abcdef", 3));
        Assert.Equal("a…", A11yTreeText.Truncate("a\U0001F600bc", 3));
    }
}
