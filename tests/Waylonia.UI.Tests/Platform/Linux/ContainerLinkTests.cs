using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Waylonia.Sessions;
using Waylonia.Tests.Ssh;
using Xunit;

namespace Waylonia.Tests;

[SupportedOSPlatform("linux")]
public sealed class ContainerLinkTests
{
    public const string DistroboxVariable = "WAYLONIA_DISTROBOX_TEST";

    public const string ToolboxVariable = "WAYLONIA_TOOLBOX_TEST";

    public const string IncusVariable = "WAYLONIA_INCUS_TEST";

    public const string IncusVmVariable = "WAYLONIA_INCUS_VM_TEST";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string Output, int Code)> RunToEndAsync(ISshLink link, string script)
    {
        using var command = await link.RunAsync(script, Ct);
        using var reader = new StreamReader(command.Output, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(Ct).WaitAsync(Limit, Ct);
        return (text, await command.Exited.WaitAsync(Limit, Ct));
    }

    internal static bool HostRuns(string pattern)
    {
        var info = new ProcessStartInfo("pgrep") { UseShellExecute = false, RedirectStandardOutput = true };
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(pattern);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Limit;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        Assert.True(condition(), what);
    }

    internal static async Task RunsSignalsAndKillsAsync(ISshLink link)
    {
        var (hello, code) = await RunToEndAsync(link, "printf 'hello %s' \"$(printf waylonia)\"; exit 3");
        Assert.Equal("hello waylonia", hello);
        Assert.Equal(3, code);

        using (var noisy = await link.RunAsync("echo one >&2; echo two >&2", Ct))
        {
            var lines = new List<string>();
            await foreach (var line in noisy.ErrorLines(Ct))
            {
                lines.Add(line);
            }

            Assert.Contains("one", lines);
            Assert.Contains("two", lines);
            Assert.DoesNotContain(lines, line => line.StartsWith(PidMarker.Prefix, StringComparison.Ordinal));
        }

        using (var sleeper = await link.RunAsync("sleep 60", Ct))
        {
            var deadline = DateTime.UtcNow + Limit;
            while (!sleeper.TrySignal("TERM") && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, Ct);
            }

            Assert.NotEqual(0, await sleeper.Exited.WaitAsync(Limit, Ct));
        }
    }

    [Theory]
    [InlineData(DistroboxVariable)]
    [InlineData(ToolboxVariable)]
    public async Task A_real_podman_box_runs_commands_takes_a_signal_leaves_nothing_behind_and_reaches_the_display(string variable)
    {
        var name = Environment.GetEnvironmentVariable(variable);
        Assert.SkipUnless(name is { Length: > 0 }, $"set {variable} to an existing container to run this test");
        var box = variable == DistroboxVariable ? PodmanBox.Distrobox : PodmanBox.Toolbox;
        var factory = new PodmanBoxLinkFactory(box);
        Assert.True(factory.IsAvailable);
        Assert.Contains(name, await factory.ListAsync(Ct));

        await using var link = factory.Create(box.Kind.For(name!), new FakePrompter());
        await link.ConnectAsync(Ct);
        Assert.True(link.IsConnected);
        Assert.Equal(ChannelShape.Direct, link.Shape);
        await RunsSignalsAndKillsAsync(link);

        var marker = Random.Shared.Next(70000, 79999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var held = await link.RunAsync($"sleep {marker}; true", Ct);
        await WaitUntilAsync(() => HostRuns($"^sleep {marker}$"), "the command started inside the container");
        held.Dispose();
        await WaitUntilAsync(() => !HostRuns($"^sleep {marker}$"), "the command outlived its disposal");

        var displayName = $"waylonia-test-{marker}";
        using var display = await link.OpenDisplayAsync(new DisplayRequest(displayName, ":9", false), Ct);
        Assert.Equal(displayName, display.ClientDisplay);
        Assert.Equal(":9", display.ClientXDisplay);
        var (_, seen) = await RunToEndAsync(link, $"test -S \"$XDG_RUNTIME_DIR/{displayName}\"");
        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task A_missing_container_is_refused_before_anything_runs()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(DistroboxVariable) is { Length: > 0 }, $"set {DistroboxVariable} to run this test");
        await using var link = new PodmanBoxLinkFactory(PodmanBox.Distrobox).Create("distrobox:no-such-box-waylonia", new FakePrompter());

        var error = await Assert.ThrowsAsync<SshLinkException>(() => link.ConnectAsync(Ct));

        Assert.Equal(SshLinkReason.Unreachable, error.Reason);
        Assert.Equal(
            "distrobox:no-such-box-waylonia could not be reached: there is no distrobox container named no-such-box-waylonia; 'distrobox list' shows the ones there are",
            error.Message);
    }

    private static async Task<(string Output, int Code)> IncusAsync(params string[] arguments)
    {
        var (code, output, error) = await CliProcess.RunAsync(Incus.Program, arguments, Ct);
        return (output + error, code);
    }

    private static async Task<IReadOnlyList<string>> DevicesAsync(string name)
    {
        var (output, _) = await IncusAsync("config", "device", "list", name);
        return output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task A_real_incus_container_runs_commands_sweeps_a_stale_device_and_mounts_the_display()
    {
        var name = Environment.GetEnvironmentVariable(IncusVariable);
        Assert.SkipUnless(name is { Length: > 0 }, $"set {IncusVariable} to an Incus container with python3 to run this test");
        var factory = new IncusLinkFactory();
        Assert.Contains(name, await factory.ListAsync(Ct));

        var root = IncusLink.StateDirectory();
        var staleDevice = "waylonia-4194300-1";
        var staleDirectory = Path.Combine(root, staleDevice);
        Directory.CreateDirectory(staleDirectory);
        var (planted, plantedCode) = await IncusAsync(Incus.AddDisk(name!, staleDevice, staleDirectory, Incus.MountPath(staleDevice)).ToArray());
        Assert.True(plantedCode == 0, planted);

        await using var link = factory.Create(DestinationKinds.Incus.For(name!), new FakePrompter());
        await link.ConnectAsync(Ct);
        Assert.Equal(ChannelShape.Direct, link.Shape);
        await RunsSignalsAndKillsAsync(link);

        var marker = Random.Shared.Next(70000, 79999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var held = await link.RunAsync($"sleep {marker}; true", Ct);
        var (_, started) = await RunToEndAsync(link, $"i=0; while ! pgrep -f '^sleep {marker}$' >/dev/null && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; pgrep -f '^sleep {marker}$' >/dev/null");
        Assert.Equal(0, started);
        held.Dispose();
        var (_, gone) = await RunToEndAsync(link, $"i=0; while pgrep -f '^sleep {marker}$' >/dev/null && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; ! pgrep -f '^sleep {marker}$' >/dev/null");
        Assert.Equal(0, gone);

        var device = $"waylonia-{Environment.ProcessId}-{marker}";
        var display = await link.OpenDisplayAsync(new DisplayRequest(device, ":97", false), Ct);
        Assert.Equal($"/run/{device}/wayland-0", display.ClientDisplay);
        Assert.Equal(":97", display.ClientXDisplay);
        var devices = await DevicesAsync(name!);
        Assert.Contains(device, devices);
        Assert.Contains(device + "-x11", devices);
        Assert.DoesNotContain(staleDevice, devices);
        Assert.False(Directory.Exists(staleDirectory));

        var (connected, connectCode) = await RunToEndAsync(
            link,
            $"python3 -c 'import socket; s = socket.socket(socket.AF_UNIX); s.connect(\"{display.ClientDisplay}\"); print(\"reached\")'");
        Assert.True(connectCode == 0, connected);
        Assert.Equal("reached", connected.Trim());

        display.Dispose();
        devices = await DevicesAsync(name!);
        Assert.DoesNotContain(device, devices);
        Assert.DoesNotContain(device + "-x11", devices);
        Assert.False(Directory.Exists(Path.Combine(root, device)));
    }

    [Fact]
    public async Task A_real_incus_vm_takes_waypipe_over_vsock_from_its_own_cid_only()
    {
        var name = Environment.GetEnvironmentVariable(IncusVmVariable);
        Assert.SkipUnless(name is { Length: > 0 }, $"set {IncusVmVariable} to an Incus VM with python3 to run this test");
        await using var link = new IncusLinkFactory().Create(DestinationKinds.Incus.For(name!), new FakePrompter());
        await link.ConnectAsync(Ct);
        Assert.Equal(ChannelShape.Waypipe, link.Shape);

        using var listener = await link.ListenAsync("/tmp/unused.sock", Ct);
        Assert.StartsWith("--vsock -s ", listener.WaypipeArgument, StringComparison.Ordinal);
        var port = listener.WaypipeArgument["--vsock -s ".Length..];
        var accepting = listener.AcceptAsync(Ct).AsTask();
        var (sent, code) = await RunToEndAsync(
            link,
            $"python3 -c 'import socket; s = socket.socket(socket.AF_VSOCK, socket.SOCK_STREAM); s.connect((2, {port})); s.sendall(b\"hi\")'");
        Assert.True(code == 0, sent);
        await using var stream = await accepting.WaitAsync(Limit, Ct);
        Assert.NotNull(stream);
        var buffer = new byte[2];
        await stream.ReadExactlyAsync(buffer, Ct);
        Assert.Equal("hi", Encoding.ASCII.GetString(buffer));

        var (listing, _) = await IncusAsync("list", "-f", "json");
        var cid = Incus.ParseInstances(listing).Single(instance => instance.Name == name).VsockId!.Value;
        using var stranger = IncusVsockListener.Bind(cid + 1, "incus:stranger");
        using var refusing = new CancellationTokenSource();
        var refused = stranger.AcceptAsync(refusing.Token).AsTask();
        var (_, strangerCode) = await RunToEndAsync(
            link,
            $"python3 -c 'import socket; s = socket.socket(socket.AF_VSOCK, socket.SOCK_STREAM); s.connect((2, {stranger.Port})); s.sendall(b\"hi\")'");
        Assert.Equal(0, strangerCode);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        Assert.False(refused.IsCompleted, "a listener took a connection from another CID");
        await refusing.CancelAsync();
        Assert.Null(await refused.WaitAsync(Limit, Ct));
    }
}
