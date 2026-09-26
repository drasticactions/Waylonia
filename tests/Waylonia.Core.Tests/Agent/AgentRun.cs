using System.Text.Json;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Shell.Nested;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class AgentRun : IAsyncDisposable
{
    private readonly string _root;
    private readonly IDisposable _gate;

    private AgentRun(string root, IDisposable gate, AgentProfile profile, AgentRuntime runtime, AgentHeadlessHost host, BasinIpcClient client)
    {
        _root = root;
        _gate = gate;
        Profile = profile;
        Runtime = runtime;
        Host = host;
        Client = client;
    }

    public AgentProfile Profile { get; }

    public AgentRuntime Runtime { get; }

    public AgentHeadlessHost Host { get; }

    public BasinIpcClient Client { get; }

    public static bool Has(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':')
            .Any(directory => directory.Length > 0 && File.Exists(Path.Combine(directory, program)));

    public static async Task<AgentRun> StartAsync(string toml)
    {
        CompositorHarness.SkipWithoutWaylandClient();
        Assert.SkipWhen(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 }, "agent runs bind sockets in XDG_RUNTIME_DIR");
        var gate = CompositorGate.Enter();
        try
        {
            return await StartGatedAsync(gate, toml);
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    private static async Task<AgentRun> StartGatedAsync(IDisposable gate, string toml)
    {
        var root = Path.Combine(Path.GetTempPath(), $"waylonia-agent-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "test"));
        File.WriteAllText(Path.Combine(root, "test", AgentProfile.FileName), toml);
        var profile = AgentProfileStore.Load(root, "test", BasinLogger.None, out var error);
        Assert.Null(error);
        profile = profile! with { Accessibility = false, Width = 640, Height = 400 };
        var socket = Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, $"wat-{Guid.NewGuid().ToString("N")[..8]}.sock");
        var runtime = new AgentRuntime(profile, headless: true) { SocketPath = socket };
        Assert.True(runtime.Prepare(out var prepareError), prepareError);
        var host = new AgentHeadlessHost(runtime, new ShellSettings(), socketName: null, xwayland: null);
        host.Start();
        var client = await BasinIpcClient.ConnectAsync(socket);
        return new AgentRun(root, gate, profile, runtime, host, client);
    }

    public async Task<JsonElement> CallAsync(string method, string json = "{}")
    {
        using var result = await Client.RawAsync($$"""{"method":"{{method}}","params":{{json}}}""");
        using var document = JsonDocument.Parse(result.Json);
        return document.RootElement.Clone();
    }

    public async Task<IpcCallException> RefusedAsync(string method, string json = "{}") =>
        await Assert.ThrowsAsync<IpcCallException>(() => CallAsync(method, json));

    public string AuditText()
    {
        Host.Stop();
        Runtime.Audit.Dispose();
        return File.ReadAllText(Runtime.Audit.Path);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Host.Dispose();
        Runtime.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        _gate.Dispose();
    }
}
