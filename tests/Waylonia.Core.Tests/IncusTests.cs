using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class IncusTests
{
    private const string Listing = """
        [
          {"name": "wl-test", "type": "container", "status": "Running", "project": "default",
           "config": {"image.os": "Debian"},
           "devices": {"waylonia-4242-1": {"type": "disk"}, "waylonia-4242-1-x11": {"type": "proxy"}},
           "expanded_devices": {"eth0": {"type": "nic"}, "root": {"type": "disk"}}},
          {"name": "wl-vm", "type": "virtual-machine", "status": "Stopped", "project": "user-1000",
           "config": {"volatile.vsock_id": "255907363"},
           "devices": {},
           "expanded_devices": {"gpu": {"type": "gpu"}}}
        ]
        """;

    [Fact]
    public void The_listing_reads_type_status_project_vsock_id_devices_and_gpu()
    {
        var instances = Incus.ParseInstances(Listing);

        var container = instances[0];
        Assert.Equal(("wl-test", false, true, "default", null, false), (container.Name, container.IsVm, container.IsRunning, container.Project, container.VsockId, container.HasGpu));
        Assert.Equal(["waylonia-4242-1", "waylonia-4242-1-x11"], container.Devices);
        var vm = instances[1];
        Assert.True(vm.IsVm);
        Assert.False(vm.IsRunning);
        Assert.Equal("user-1000", vm.Project);
        Assert.Equal(255907363u, vm.VsockId);
        Assert.True(vm.HasGpu);
    }

    [Fact]
    public void A_script_runs_as_the_user_in_its_home_through_a_wrapper_that_makes_its_own_process_group()
    {
        var user = new IncusUser("tester", 1000, 1000, "/home/tester");

        Assert.Equal(
            [
                "exec", "-T", "web", "--user", "1000", "--group", "1000", "--cwd", "/home/tester",
                "--env", "HOME=/home/tester", "--env", "USER=tester", "--env", "LOGNAME=tester",
                "--", "sh", "-c",
                Incus.Wrapper,
                "sh", "echo hi",
            ],
            Incus.Script("web", user, "echo hi"));
        Assert.Equal(
            ["exec", "-T", "web", "--", "sh", "-c", "kill -s TERM -- -42 2>/dev/null || kill -s TERM 42"],
            Incus.Signal("web", "TERM", 42));
        Assert.Equal(["start", "web"], Incus.Start("web"));
        Assert.Equal(["exec", "-T", "web", "--", "getent", "passwd", "1000"], Incus.Passwd("web", "1000"));
    }

    [Fact]
    public void The_display_is_a_disk_device_and_x11_a_proxy_that_connects_as_this_user()
    {
        Assert.Equal(
            ["config", "device", "add", "web", "waylonia-7-1", "disk", "source=/home/da/.local/state/waylonia/incus/waylonia-7-1", "path=/run/waylonia-7-1"],
            Incus.AddDisk("web", "waylonia-7-1", "/home/da/.local/state/waylonia/incus/waylonia-7-1", Incus.MountPath("waylonia-7-1")));
        Assert.Equal("/run/waylonia-7-1/wayland-0", Incus.ClientDisplay("waylonia-7-1"));
        Assert.Equal(
            [
                "config", "device", "add", "web", "waylonia-7-1-x11", "proxy", "bind=container",
                "listen=unix:/tmp/.X11-unix/X2", "connect=unix:/tmp/.X11-unix/X2",
                "security.uid=1000", "security.gid=1000", "mode=0777",
            ],
            Incus.AddX11Proxy("web", Incus.X11Device("waylonia-7-1"), 2, 1000, 1000));
        Assert.Equal(["config", "device", "remove", "web", "waylonia-7-1"], Incus.RemoveDevice("web", "waylonia-7-1"));
    }

    [Fact]
    public void The_wrapper_makes_a_group_only_where_the_shell_does_not_lead_one_since_setsid_forks_a_leader()
    {
        Assert.StartsWith("echo \"waylonia-pid $$\" >&2; ", Incus.Wrapper, StringComparison.Ordinal);
        Assert.Contains("if [ \"$(cut -d ' ' -f 5 /proc/$$/stat 2>/dev/null)\" != \"$$\" ]", Incus.Wrapper, StringComparison.Ordinal);
        Assert.EndsWith("then exec setsid sh -c \"$1\"; fi; exec sh -c \"$1\"", Incus.Wrapper, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(":2", 2)]
    [InlineData(":10.0", 10)]
    [InlineData("localhost:2", null)]
    [InlineData(":", null)]
    [InlineData(null, null)]
    public void The_display_number_comes_from_a_local_display_name(string? display, int? number)
    {
        Assert.Equal(number, Incus.DisplayNumber(display));
    }

    [Fact]
    public void A_passwd_line_names_the_account()
    {
        Assert.Equal(new IncusUser("tester", 1000, 1001, "/home/tester"), Incus.ParsePasswd("tester:x:1000:1001::/home/tester:/bin/sh\n"));
        Assert.Null(Incus.ParsePasswd(string.Empty));
        Assert.Null(Incus.ParsePasswd("broken:x:a:b::/:/bin/sh"));
    }

    [Fact]
    public void The_sweep_takes_only_waylonia_devices_whose_process_is_gone()
    {
        var devices = new[] { "eth0", "waylonia-100-1", "waylonia-100-1-x11", "waylonia-200-3", "waylonia-x", "gpu" };

        var stale = Incus.StaleDevices(devices, pid => pid == 200);

        Assert.Equal(["waylonia-100-1", "waylonia-100-1-x11"], stale);
        Assert.Equal(100, Incus.DevicePid("waylonia-100-1-x11"));
        Assert.Null(Incus.DevicePid("waylonia-x"));
        Assert.Null(Incus.DevicePid("eth0"));
    }
}
