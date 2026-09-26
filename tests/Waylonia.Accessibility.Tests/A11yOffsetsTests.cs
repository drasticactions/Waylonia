using Xunit;

namespace Waylonia.Accessibility.Tests;

public sealed class A11yOffsetsTests
{
    private static A11yWindowTarget Shadowed() => new(
        Pid: 10,
        Title: "demo",
        ClientWidth: 800,
        ClientHeight: 600,
        SurfaceWidth: 846,
        SurfaceHeight: 646,
        GeometryX: 23,
        GeometryY: 23);

    [Fact]
    public void A_root_the_size_of_the_surface_counts_from_the_surface_so_the_shadow_is_added()
    {
        Assert.Equal((23, 23), A11yOffsets.For(new A11yBox(0, 0, 846, 646), Shadowed()));
    }

    [Fact]
    public void A_root_the_size_of_the_client_area_counts_from_itself()
    {
        Assert.Equal((0, 0), A11yOffsets.For(new A11yBox(0, 0, 800, 600), Shadowed()));
    }

    [Fact]
    public void A_root_that_is_not_at_the_origin_moves_the_offset_with_it()
    {
        Assert.Equal((5, 7), A11yOffsets.For(new A11yBox(5, 7, 800, 600), Shadowed()));
        Assert.Equal((28, 30), A11yOffsets.For(new A11yBox(5, 7, 846, 646), Shadowed()));
    }

    [Fact]
    public void A_size_within_the_tolerance_still_matches_the_surface()
    {
        Assert.Equal((23, 23), A11yOffsets.For(new A11yBox(0, 0, 849, 643), Shadowed()));
    }

    [Fact]
    public void A_root_of_another_size_falls_back_to_its_own_position()
    {
        Assert.Equal((3, 4), A11yOffsets.For(new A11yBox(3, 4, 500, 300), Shadowed()));
    }

    [Fact]
    public void A_window_without_a_shadow_never_adds_a_geometry_offset()
    {
        var plain = new A11yWindowTarget(10, "kwrite", 640, 480, 640, 480, 0, 0);
        Assert.Equal((0, 0), A11yOffsets.For(new A11yBox(0, 0, 640, 480), plain));
    }

    [Fact]
    public void Distance_sums_both_sides()
    {
        Assert.Equal(7, A11yOffsets.Distance(new A11yBox(0, 0, 803, 596), 800, 600));
        Assert.True(A11yOffsets.Near(new A11yBox(0, 0, 804, 596), 800, 600));
        Assert.False(A11yOffsets.Near(new A11yBox(0, 0, 805, 600), 800, 600));
    }

    [Theory]
    [InlineData(0, 0, 800, 600, true)]
    [InlineData(-24, -21, 10, 10, true)]
    [InlineData(1115850253, -1115839634, 0, 0, false)]
    [InlineData(0, 0, -1, 10, false)]
    [InlineData(int.MinValue, 0, 1, 1, false)]
    public void Uninitialized_extents_are_not_a_box(int x, int y, int width, int height, bool plausible) =>
        Assert.Equal(plausible, A11yOffsets.IsPlausible(x, y, width, height));
}
