using Xunit;

namespace Waylonia.Accessibility.Tests;

public sealed class A11yNodeRefTests
{
    [Fact]
    public void An_id_splits_at_the_colon_before_the_path()
    {
        Assert.True(A11yNodeRef.TryParse(":1.42:/org/a11y/atspi/accessible/7", out var node));
        Assert.Equal(":1.42", node.Bus);
        Assert.Equal("/org/a11y/atspi/accessible/7", node.Path);
        Assert.Equal(":1.42:/org/a11y/atspi/accessible/7", node.Id);
    }

    [Fact]
    public void A_well_known_bus_name_works_too()
    {
        Assert.True(A11yNodeRef.TryParse("org.a11y.atspi.Registry:/org/a11y/atspi/accessible/root", out var node));
        Assert.Equal("org.a11y.atspi.Registry", node.Bus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData(":1.42")]
    [InlineData(":/org/a11y")]
    [InlineData(":1.42:/org//a11y")]
    [InlineData(":1.42:/org/a11y/")]
    [InlineData(":1.42:/org/a-11y")]
    public void Anything_else_is_refused_with_a_sentence(string id)
    {
        Assert.False(A11yNodeRef.TryParse(id, out _));
        var error = Assert.Throws<A11yException>(() => A11yNodeRef.Parse(id));
        Assert.Contains("is not a node id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_null_reference_is_recognized()
    {
        Assert.True(new A11yNodeRef("", "/org/a11y/atspi/null").IsNull);
        Assert.True(new A11yNodeRef(":1.2", A11yNodeRef.NullPath).IsNull);
        Assert.False(new A11yNodeRef(":1.2", "/org/a11y/atspi/accessible/root").IsNull);
    }

    [Theory]
    [InlineData("org.a11y.atspi.Event.Object", true)]
    [InlineData("org.a11y.atspi.Event.Window", true)]
    [InlineData("org.a11y.atspi.Cache", true)]
    [InlineData("org.a11y.atspi.Registry", false)]
    [InlineData("org.freedesktop.DBus", false)]
    public void Only_event_and_cache_signals_count_as_events(string @interface, bool counts)
    {
        Assert.Equal(counts, A11yBus.IsEvent(@interface));
    }
}
