using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class PodmanBoxTests
{
    [Fact]
    public void Distrobox_enters_without_a_workdir_and_toolbox_runs_with_a_container_both_through_the_pid_wrapper()
    {
        Assert.Equal(
            ["enter", "--no-workdir", "dev", "--", "sh", "-c", "echo \"waylonia-pid $$\" >&2; exec sh -c \"$1\"", "sh", "echo hi"],
            PodmanBox.Distrobox.Run("dev", "echo hi"));
        Assert.Equal(
            ["run", "-c", "fedora-toolbox-42", "sh", "-c", "echo \"waylonia-pid $$\" >&2; exec sh -c \"$1\"", "sh", "echo hi"],
            PodmanBox.Toolbox.Run("fedora-toolbox-42", "echo hi"));
        Assert.Equal("distrobox", PodmanBox.Distrobox.Program);
        Assert.Equal(["list", "--no-color"], PodmanBox.Distrobox.ListArguments);
        Assert.Equal(["list", "--containers"], PodmanBox.Toolbox.ListArguments);
        Assert.Same(PodmanBox.Toolbox, PodmanBox.For(DestinationKinds.Toolbox));
        Assert.Null(PodmanBox.For(DestinationKinds.Incus));
    }

    [Fact]
    public void Distrobox_starts_with_a_cleared_environment_and_toolbox_keeps_its_own_forwarding()
    {
        var host = new Dictionary<string, string>
        {
            ["HOME"] = "/home/da",
            ["USER"] = "da",
            ["PATH"] = "/usr/bin",
            ["XDG_RUNTIME_DIR"] = "/run/user/1000",
            ["LANG"] = "en_US.UTF-8",
            ["CONTAINER_MANAGER"] = "podman",
            ["GITHUB_TOKEN"] = "secret",
            ["SSH_AUTH_SOCK"] = "/run/user/1000/ssh",
            ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus",
            ["WAYLAND_DISPLAY"] = "wayland-1",
        };

        var cleared = PodmanBox.Distrobox.Environment(host)!;

        Assert.Equal(
            ["CONTAINER_MANAGER", "HOME", "LANG", "PATH", "USER", "XDG_RUNTIME_DIR"],
            cleared.Keys.Order(StringComparer.Ordinal));
        Assert.Null(PodmanBox.Toolbox.Environment(host));
    }

    [Fact]
    public void The_lists_read_the_name_column()
    {
        Assert.Equal(
            ["wl-box", "dev"],
            PodmanBox.Distrobox.ParseList(
                "ID           | NAME                 | STATUS             | IMAGE                         \n" +
                "f3c8ca7c4a92 | wl-box               | Up 37 minutes      | docker.io/library/debian:13   \n" +
                "0123456789ab | dev                  | Exited (0) 2 days  | quay.io/fedora/fedora:42      \n"));
        Assert.Equal(
            ["wl-tb"],
            PodmanBox.Toolbox.ParseList(
                "CONTAINER ID  CONTAINER NAME  CREATED         STATUS   IMAGE NAME\n" +
                "60577331567d  wl-tb           37 minutes ago  running  quay.io/toolbx/arch-toolbox:latest\n"));
        Assert.Empty(PodmanBox.Toolbox.ParseList(string.Empty));
    }
}
