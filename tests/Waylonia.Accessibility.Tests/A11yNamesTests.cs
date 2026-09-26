using Xunit;

namespace Waylonia.Accessibility.Tests;

public sealed class A11yNamesTests
{
    [Theory]
    [InlineData(AtSpiRole.PushButton, "push-button")]
    [InlineData(AtSpiRole.Frame, "frame")]
    [InlineData(AtSpiRole.FontChooser, "font-chooser")]
    [InlineData(AtSpiRole.Heading, "heading")]
    [InlineData(AtSpiRole.Static, "static")]
    [InlineData(AtSpiRole.Switch, "switch")]
    [InlineData(AtSpiRole.Accelerator, "accelerator-label")]
    [InlineData(AtSpiRole.ToolTip, "tool-tip")]
    public void Roles_are_named_as_AT_SPI_names_them(AtSpiRole role, string name)
    {
        Assert.Equal(name, A11yNames.Role((uint)role));
    }

    [Fact]
    public void The_role_numbers_follow_atspi_constants()
    {
        Assert.Equal(83, (int)AtSpiRole.Heading);
        Assert.Equal(98, (int)AtSpiRole.ListBox);
        Assert.Equal(115, (int)AtSpiRole.Timer);
        Assert.Equal(130, (int)AtSpiRole.Switch);
        foreach (var role in Enum.GetValues<AtSpiRole>())
        {
            Assert.DoesNotContain("role-", A11yNames.Role((uint)role), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_unknown_role_is_named_by_its_number()
    {
        Assert.Equal("role-900", A11yNames.Role(900));
    }

    [Fact]
    public void A_state_set_is_read_bit_by_bit_across_both_words()
    {
        uint low = (1u << (int)AtSpiState.Enabled) | (1u << (int)AtSpiState.Showing) | (1u << (int)AtSpiState.Focused);
        uint high = 1u << ((int)AtSpiState.ReadOnly - 32);
        Assert.Equal(["enabled", "focused", "showing", "read-only"], A11yNames.States([low, high]));
    }

    [Fact]
    public void Has_reads_one_state()
    {
        uint[] bits = [1u << (int)AtSpiState.Showing, 0];
        Assert.True(A11yNames.Has(bits, AtSpiState.Showing));
        Assert.False(A11yNames.Has(bits, AtSpiState.Visible));
        Assert.False(A11yNames.Has([], AtSpiState.Showing));
    }

    [Theory]
    [InlineData("push-button")]
    [InlineData("Push Button")]
    [InlineData("PUSH_BUTTON")]
    public void A_role_name_matches_whatever_its_case_and_separator(string wanted)
    {
        Assert.True(A11yNames.RoleMatches(wanted, (uint)AtSpiRole.PushButton));
        Assert.False(A11yNames.RoleMatches(wanted, (uint)AtSpiRole.ToggleButton));
    }

    [Fact]
    public void The_window_roles_are_frame_window_dialog_and_alert()
    {
        Assert.True(A11yNames.IsWindowRole((uint)AtSpiRole.Frame));
        Assert.True(A11yNames.IsWindowRole((uint)AtSpiRole.Window));
        Assert.True(A11yNames.IsWindowRole((uint)AtSpiRole.Dialog));
        Assert.True(A11yNames.IsWindowRole((uint)AtSpiRole.Alert));
        Assert.False(A11yNames.IsWindowRole((uint)AtSpiRole.Panel));
    }
}
