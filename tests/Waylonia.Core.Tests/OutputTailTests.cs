using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class OutputTailTests
{
    [Fact]
    public void The_tail_keeps_the_last_lines_only()
    {
        var tail = new OutputTail(3);
        foreach (var line in new[] { "a", "b", "c", "d" })
        {
            tail.Add(line);
        }

        Assert.Equal(["b", "c", "d"], tail.Lines());
        Assert.Equal("d", tail.LastLine());
        tail.Clear();
        Assert.Empty(tail.Lines());
        Assert.Null(tail.LastLine());
    }
}
