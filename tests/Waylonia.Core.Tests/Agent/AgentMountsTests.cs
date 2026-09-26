using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentMountsTests
{
    private const string MountInfo = """
        22 1 0:21 / / rw,relatime shared:1 - btrfs /dev/nvme0n1p2 rw
        60 22 0:52 / /run/user/1000 rw,nosuid,nodev,relatime shared:30 - tmpfs tmpfs rw,size=3272040k,mode=700,uid=1000,gid=1000
        61 60 0:53 / /run/user/1000/gvfs rw,nosuid,nodev,relatime shared:31 - fuse.gvfsd-fuse gvfsd-fuse rw,user_id=1000,group_id=1000
        70 60 0:61 / /run/user/1000/waylonia-agent-claude-42/gvfs rw,nosuid,nodev,relatime shared:40 - fuse.gvfsd-fuse gvfsd-fuse rw,user_id=1000,group_id=1000
        71 60 0:62 / /run/user/1000/waylonia-agent-claude-42/doc rw,nosuid,nodev,relatime shared:41 - fuse.portal portal rw,user_id=1000,group_id=1000
        72 71 0:63 / /run/user/1000/waylonia-agent-claude-42/doc/by-app rw,relatime shared:42 - fuse.portal portal rw
        73 60 0:64 / /run/user/1000/waylonia-agent-claude-420/doc rw,relatime shared:43 - fuse.portal portal rw

        """;

    [Fact]
    public void The_mounts_inside_the_runtime_directory_are_found_deepest_first()
    {
        var mounts = AgentMounts.Under("/run/user/1000/waylonia-agent-claude-42", MountInfo);
        Assert.Equal(
            [
                "/run/user/1000/waylonia-agent-claude-42/doc/by-app",
                "/run/user/1000/waylonia-agent-claude-42/gvfs",
                "/run/user/1000/waylonia-agent-claude-42/doc",
            ],
            mounts);
    }

    [Fact]
    public void A_sibling_whose_name_starts_the_same_is_not_inside()
    {
        Assert.DoesNotContain(
            "/run/user/1000/waylonia-agent-claude-420/doc",
            AgentMounts.Under("/run/user/1000/waylonia-agent-claude-42/", MountInfo));
    }

    [Fact]
    public void An_escaped_mount_point_is_decoded()
    {
        const string info = "80 60 0:70 / /run/agent\\040one/doc rw - fuse.portal portal rw\n";
        Assert.Equal(["/run/agent one/doc"], AgentMounts.Under("/run/agent one", info));
    }

    [Fact]
    public void Nothing_mounted_inside_gives_nothing()
    {
        Assert.Empty(AgentMounts.Under("/run/user/1000/waylonia-agent-other-1", MountInfo));
    }
}
