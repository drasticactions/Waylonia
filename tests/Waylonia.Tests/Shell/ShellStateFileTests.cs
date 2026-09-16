using Basin.Diagnostics;
using Tomlyn;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ShellStateFileTests
{
    [Fact]
    public void A_state_round_trips_through_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"waylonia-state-{Guid.NewGuid():N}", "shell.toml");
        try
        {
            var state = new ShellWindowState(1440, 900, 120, 80, true);
            ShellStateFile.Save(path, state, BasinLogger.None);
            Assert.Equal(state, ShellStateFile.Load(path, BasinLogger.None));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path)!);
            }
        }
    }

    [Fact]
    public void A_missing_file_and_a_tiny_size_fall_back_to_the_default()
    {
        Assert.Equal(ShellWindowState.Default, ShellStateFile.Load(Path.Combine(Path.GetTempPath(), "waylonia-nowhere", "shell.toml"), BasinLogger.None));
        var parsed = ShellStateFile.Parse(Toml.ToModel("width = 10\nheight = 10\nx = 3\ny = 4\n"));
        Assert.Equal(ShellWindowState.DefaultWidth, parsed.Width);
        Assert.Equal(ShellWindowState.DefaultHeight, parsed.Height);
        Assert.Equal(3, parsed.X);
        Assert.False(parsed.FullScreen);
    }

    [Fact]
    public void A_state_without_a_position_writes_none()
    {
        var text = ShellStateFile.Render(new ShellWindowState(1280, 800, null, null, false));
        Assert.Equal("width = 1280\nheight = 800\n", text);
    }
}
