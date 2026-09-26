using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentEnvironmentTests
{
    private static readonly AgentProfile Profile = new("test", "/state/agents/test");

    [Fact]
    public void A_launched_application_gets_the_profile_home_and_its_own_runtime()
    {
        var environment = AgentEnvironment.Build(Profile, "/run/user/1000/waylonia-agent-test-42", ":3", "unix:path=/tmp/dbus-x");
        var env = environment.Env;
        Assert.Equal("/state/agents/test/home", env["HOME"]);
        Assert.Equal("/state/agents/test/home/.config", env["XDG_CONFIG_HOME"]);
        Assert.Equal("/state/agents/test/home/.local/share", env["XDG_DATA_HOME"]);
        Assert.Equal("/state/agents/test/home/.local/state", env["XDG_STATE_HOME"]);
        Assert.Equal("/state/agents/test/home/.cache", env["XDG_CACHE_HOME"]);
        Assert.Equal("/run/user/1000/waylonia-agent-test-42", env["XDG_RUNTIME_DIR"]);
        Assert.Equal(AgentEnvironment.WaylandName, env["WAYLAND_DISPLAY"]);
        Assert.Equal(":3", env["DISPLAY"]);
        Assert.Equal("unix:path=/tmp/dbus-x", env["DBUS_SESSION_BUS_ADDRESS"]);
        Assert.Equal("wayland", env["XDG_SESSION_TYPE"]);
        Assert.Equal("atspi", env["GTK_A11Y"]);
        Assert.Equal("1", env["QT_LINUX_ACCESSIBILITY_ALWAYS_ON"]);
        Assert.Equal("1", env["ACCESSIBILITY_ENABLED"]);
        Assert.Contains("NO_AT_BRIDGE", environment.Unset);
        Assert.Contains("SSH_AUTH_SOCK", environment.Unset);
        Assert.Contains("XDG_ACTIVATION_TOKEN", environment.Unset);
        Assert.Contains("BASIN_SOCKET", environment.Unset);
        Assert.DoesNotContain("DISPLAY", environment.Unset);
        Assert.DoesNotContain("PATH", environment.Unset);
        Assert.DoesNotContain("LANG", environment.Unset);
    }

    [Fact]
    public void Without_an_x_display_or_a_bus_the_host_ones_are_removed()
    {
        var environment = AgentEnvironment.Build(Profile, "/run/agent", null, null);
        Assert.False(environment.Env.ContainsKey("DISPLAY"));
        Assert.Contains("DISPLAY", environment.Unset);
        Assert.Contains("DBUS_SESSION_BUS_ADDRESS", environment.Unset);

        var applied = environment.Apply(new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["LANG"] = "en_US.UTF-8",
            ["XDG_DATA_DIRS"] = "/usr/share",
            ["DISPLAY"] = ":0",
            ["WAYLAND_DISPLAY"] = "wayland-1",
            ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus",
            ["NO_AT_BRIDGE"] = "1",
            ["HOME"] = "/home/da",
        });
        Assert.Equal("/usr/bin", applied["PATH"]);
        Assert.Equal("en_US.UTF-8", applied["LANG"]);
        Assert.Equal("/usr/share", applied["XDG_DATA_DIRS"]);
        Assert.False(applied.ContainsKey("DISPLAY"));
        Assert.False(applied.ContainsKey("DBUS_SESSION_BUS_ADDRESS"));
        Assert.False(applied.ContainsKey("NO_AT_BRIDGE"));
        Assert.Equal(AgentEnvironment.WaylandName, applied["WAYLAND_DISPLAY"]);
        Assert.Equal("/state/agents/test/home", applied["HOME"]);
    }
}
