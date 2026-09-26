using System.Text.Json;
using Basin.Ipc;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

[Collection(AgentRunCollection.Name)]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class AgentHeadlessHostTests
{
    private static async Task<IReadOnlyList<string>> MethodsAsync(AgentRun run) =>
        (await run.CallAsync("ipc/methods")).GetProperty("methods").EnumerateArray().Select(static name => name.GetString()!).ToArray();

    private static async Task<bool> RunningAsync(AgentRun run, long launch)
    {
        foreach (var process in (await run.CallAsync("process/list")).GetProperty("processes").EnumerateArray())
        {
            if (process.GetProperty("launch_id").GetInt64() == launch)
            {
                return process.GetProperty("running").GetBoolean();
            }
        }

        return false;
    }

    private static async Task UntilAsync(Func<Task<bool>> settled, string what)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(what);
    }

    [Fact]
    public async Task The_socket_leaves_out_spawn_and_seat_and_offers_the_agent_methods()
    {
        await using var run = await AgentRun.StartAsync("[launch]\nallow = [\"sleep\"]\n");
        var methods = await MethodsAsync(run);
        Assert.DoesNotContain("process/spawn", methods);
        Assert.DoesNotContain(methods, static name => name.StartsWith("seat/", StringComparison.Ordinal));
        foreach (var name in new[] { "waylonia/describe", "waylonia/launch", "waylonia/launchable", "waylonia/window-info", "process/kill", "capture/window", "input/pointer-button", "approval/answer" })
        {
            Assert.Contains(name, methods);
        }

        Assert.DoesNotContain(methods, static name => name.StartsWith("waylonia/a11y-", StringComparison.Ordinal));
        var describe = await run.CallAsync("waylonia/describe");
        Assert.Equal("headless", describe.GetProperty("mode").GetString());
        Assert.Equal(640, describe.GetProperty("output").GetProperty("width").GetInt32());
        Assert.Contains("[accessibility] enabled = false", describe.GetProperty("accessibility").GetString(), StringComparison.Ordinal);
        Assert.Equal(["process/spawn", "seat/*"], describe.GetProperty("omitted").EnumerateArray().Select(static e => e.GetString()));
        Assert.EndsWith("/wayland-0", describe.GetProperty("wayland_display").GetString(), StringComparison.Ordinal);
        Assert.True(File.Exists(describe.GetProperty("wayland_display").GetString()));
    }

    [Fact]
    public async Task An_allowlisted_program_starts_and_an_unlisted_one_is_refused()
    {
        await using var run = await AgentRun.StartAsync("[launch]\nallow = [\"sleep\"]\nask = false\n");
        var launched = await run.CallAsync("waylonia/launch", """{"command":"sleep","args":["30"]}""");
        var launch = launched.GetProperty("launch_id").GetInt64();
        Assert.True(launched.GetProperty("listed").GetBoolean());
        Assert.StartsWith(run.Profile.Logs, launched.GetProperty("log").GetString(), StringComparison.Ordinal);
        Assert.True(await RunningAsync(run, launch));

        var refused = await run.RefusedAsync("waylonia/launch", """{"command":"true"}""");
        Assert.Equal("refused", refused.Code);
        Assert.Contains("not in the allowlist", refused.Reason, StringComparison.Ordinal);

        await run.CallAsync("process/kill", $$"""{"launch_id":{{launch}}}""");
        await UntilAsync(async () => !await RunningAsync(run, launch), "the killed launch never exited");

        var audit = run.AuditText();
        Assert.Contains("\"method\":\"waylonia/launch\",\"params\":{\"command\":\"sleep\"", audit, StringComparison.Ordinal);
        Assert.Contains("not in the allowlist", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_one_to_ask_an_unlisted_launch_is_denied_at_once()
    {
        await using var run = await AgentRun.StartAsync("[launch]\nallow = []\nask = true\n");
        var refused = await run.RefusedAsync("waylonia/launch", """{"command":"true"}""");
        Assert.Contains("approval/requested", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unlisted_launch_waits_for_approval_and_runs_once_allowed()
    {
        await using var run = await AgentRun.StartAsync("[launch]\nallow = []\nask = true\n");
        await using var person = await BasinIpcClient.ConnectAsync(run.Runtime.SocketPath, TestContext.Current.CancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(20));
        var subscribed = new TaskCompletionSource();
        var requests = new List<JsonElement>();
        async Task AnswerAsync()
        {
            var answered = 0;
            await foreach (var e in person.SubscribeAsync(["approval/requested"], () => subscribed.TrySetResult(), stop.Token))
            {
                using var document = JsonDocument.Parse(e.Data);
                var request = document.RootElement.Clone();
                requests.Add(request);
                var answer = answered++ == 0 ? "allow_once" : "deny";
                using var _ = await person.RawAsync(
                    $$$"""{"method":"approval/answer","params":{"id":{{{request.GetProperty("id").GetInt64()}}},"answer":"{{{answer}}}"}}""",
                    stop.Token);
                if (answered == 2)
                {
                    return;
                }
            }
        }

        var answering = Task.Run(AnswerAsync, stop.Token);
        await subscribed.Task.WaitAsync(stop.Token);

        var launched = await run.CallAsync("waylonia/launch", """{"command":"sleep","args":["20"]}""");
        Assert.False(launched.GetProperty("listed").GetBoolean());
        var denied = await run.RefusedAsync("waylonia/launch", """{"command":"sleep","args":["20"]}""");
        Assert.Contains("denied", denied.Reason, StringComparison.Ordinal);
        await answering.WaitAsync(stop.Token);

        Assert.Equal("waylonia/launch", requests[0].GetProperty("method").GetString());
        Assert.Contains("sleep 20", requests[0].GetProperty("reason").GetString(), StringComparison.Ordinal);
        await run.CallAsync("process/kill", $$"""{"launch_id":{{launched.GetProperty("launch_id").GetInt64()}}}""");

        var audit = run.AuditText();
        Assert.Contains("\"event\":\"approval-answered\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"approval\":\"allow_once\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"approval\":\"deny\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Input_is_refused_while_a_person_holds_the_window()
    {
        await using var run = await AgentRun.StartAsync("[launch]\nallow = []\n");
        await run.CallAsync("input/pointer-move", """{"x":10,"y":10}""");
        run.Host.Post(() => run.Runtime.HostInput(pressOrKey: true));
        await UntilAsync(() => Task.FromResult(run.Runtime.Takeover.IsPaused), "the host press never paused the agent");
        var refused = await run.RefusedAsync("input/pointer-move", """{"x":20,"y":20}""");
        Assert.Equal(AgentTakeover.PausedMessage, refused.Reason);
        Assert.Equal("Agent: paused", (await run.CallAsync("waylonia/describe")).GetProperty("takeover").GetString());
        await run.CallAsync("capture/output", """{"output":"shell","to":"inline"}""");

        run.Host.Post(run.Runtime.Resume);
        await UntilAsync(() => Task.FromResult(!run.Runtime.Takeover.IsPaused), "resume never ended the pause");
        await run.CallAsync("input/pointer-move", """{"x":20,"y":20}""");
        var audit = run.AuditText();
        Assert.Contains("\"event\":\"takeover\",\"state\":\"paused\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"driving\",\"by\":\"button\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Window_info_names_the_launch_that_owns_a_window()
    {
        Assert.SkipUnless(AgentRun.Has("foot"), "this row launches foot");
        await using var run = await AgentRun.StartAsync("[launch]\nallow = [\"foot\"]\n");
        var launch = (await run.CallAsync("waylonia/launch", """{"command":"foot"}""")).GetProperty("launch_id").GetInt64();
        var window = await run.CallAsync("windows/wait", """{"app_id":"foot","timeout_ms":10000}""");
        var id = window.GetProperty("id").GetUInt64();
        await UntilAsync(
            async () => (await run.CallAsync("windows/get", $$"""{"id":{{id}}}""")).GetProperty("state").GetProperty("maximized").GetBoolean(),
            "the foot window never maximized");

        JsonElement row = default;
        await UntilAsync(async () =>
        {
            var rows = (await run.CallAsync("waylonia/window-info", $$"""{"id":{{id}}}""")).GetProperty("windows");
            if (rows.GetArrayLength() == 0)
            {
                return false;
            }

            row = rows[0];
            return true;
        }, "the foot window never mapped");
        Assert.Equal(launch, row.GetProperty("launched").GetInt64());
        Assert.False(row.GetProperty("x11").GetBoolean());
        Assert.False(row.GetProperty("dialog").GetBoolean());

        var capture = await run.CallAsync("capture/window", $$"""{"id":{{id}},"to":"inline"}""");
        Assert.True(capture.GetProperty("width").GetInt32() > 0);
        await run.CallAsync("process/kill", $$"""{"launch_id":{{launch}}}""");
        await run.CallAsync("windows/wait-idle", "{\"timeout_ms\":2000}").ContinueWith(static _ => { }, TaskScheduler.Default);
    }
}
