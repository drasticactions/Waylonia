using Xunit;

namespace Waylonia.Accessibility.Tests.Live;

public sealed class LiveGtkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Log(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    private static async Task<(LiveEnvironment Environment, A11yBus Bus)> StartAsync()
    {
        var environment = await LiveEnvironment.StartAsync(Ct);
        try
        {
            await A11yBus.EnableAsync(environment.SessionBus, Ct);
            await environment.StartRegistryAsync(Ct);
            var bus = await A11yBus.ConnectAsync(environment.SessionBus, Ct);
            return (environment, bus);
        }
        catch
        {
            await environment.DisposeAsync();
            throw;
        }
    }

    private static async Task<A11yWindow> PairAsync(A11yService service, A11yWindowTarget target)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            if (await service.FindWindowAsync(target, Ct) is { } window)
            {
                return window;
            }

            Assert.True(DateTime.UtcNow < deadline, $"no accessible window paired with pid {target.Pid} \"{target.Title}\"");
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task A_zenity_question_is_answered_by_clicking_its_Yes_button()
    {
        var missing = LiveEnvironment.Missing("zenity");
        Assert.SkipWhen(missing is not null, missing ?? string.Empty);
        var (environment, bus) = await StartAsync();
        await using var _ = environment;
        await using var __ = bus;
        var service = new A11yService(bus);

        var zenity = environment.Launch(
            "zenity",
            "--question",
            "--title=Waylonia accessibility",
            "--text=Proceed with the accessibility test?");
        var target = await environment.Compositor.WaitForWindowAsync(
            window => window.Pid == zenity.Id, TimeSpan.FromSeconds(20), Ct);
        Log($"target: {target}");

        var window = await PairAsync(service, target);
        Log($"window: {window}");
        Assert.Equal(zenity.Id, window.Pid);

        var applications = await service.ApplicationsAsync(Ct);
        Assert.Contains(applications, app => app.Pid == zenity.Id);

        var prompt = await service.WaitTextAsync(window, "proceed with the", TimeSpan.FromSeconds(10), Ct);
        Assert.NotNull(prompt);
        Log($"prompt: {prompt}");

        var tree = await service.TreeTextAsync(window, 20, 400, Ct);
        Log(tree);
        Assert.StartsWith($"[{window.RootId}] ", tree, StringComparison.Ordinal);

        var buttons = await service.FindAsync(window, "push-button", "yes", ["showing"], 5, Ct);
        var yes = Assert.Single(buttons);
        Log($"yes: {yes}");
        Assert.NotNull(yes.Box);
        Assert.InRange(yes.Box.Value.X, 0, target.ClientWidth);
        Assert.InRange(yes.Box.Value.Y, 0, target.ClientHeight);
        Assert.Equal(yes.Box, await service.ExtentsAsync(window, yes.Id, Ct));

        var actions = await service.ActionsAsync(yes.Id, Ct);
        Assert.Contains("click", actions, StringComparer.OrdinalIgnoreCase);
        var unknown = await Assert.ThrowsAsync<A11yException>(() => service.DoActionAsync(yes.Id, "juggle", Ct));
        Assert.Contains("click", unknown.Message, StringComparison.Ordinal);

        var editable = await Assert.ThrowsAsync<A11yException>(() => service.SetTextAsync(yes.Id, "no", Ct));
        Assert.Contains("is not editable text", editable.Message, StringComparison.Ordinal);

        var bogus = $"{yes.Id[..yes.Id.IndexOf(":/", StringComparison.Ordinal)]}:/org/gnome/Zenity/a11y/nothing_here";
        var missingNode = await Assert.ThrowsAsync<A11yException>(() => service.ActionsAsync(bogus, Ct));
        Assert.Equal($"node {bogus} no longer exists", missingNode.Message);

        await service.DoActionAsync(yes.Id, "click", Ct);
        await zenity.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(0, zenity.ExitCode);

        var gone = await Assert.ThrowsAsync<A11yException>(() => service.DoActionAsync(yes.Id, null, Ct));
        Log($"after exit: {gone.Message}");
        Assert.Contains(yes.Id, gone.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_set_on_a_zenity_entry_is_seen_through_events_and_returned_on_OK()
    {
        var missing = LiveEnvironment.Missing("zenity");
        Assert.SkipWhen(missing is not null, missing ?? string.Empty);
        var (environment, bus) = await StartAsync();
        await using var _ = environment;
        await using var __ = bus;
        var service = new A11yService(bus);
        var events = 0;
        bus.EventReceived += () => Interlocked.Increment(ref events);

        var zenity = environment.Launch("zenity", "--entry", "--title=Waylonia entry", "--text=Type a word");
        var target = await environment.Compositor.WaitForWindowAsync(
            window => window.Pid == zenity.Id, TimeSpan.FromSeconds(20), Ct);
        var window = await PairAsync(service, target);
        Assert.NotNull(await service.WaitTextAsync(window, "Type a word", TimeSpan.FromSeconds(10), Ct));

        var entries = await service.FindAsync(window, "text", null, ["editable", "showing"], 5, Ct);
        var entry = Assert.Single(entries);
        Log($"entry: {entry}");
        try
        {
            await service.GrabFocusAsync(entry.Id, Ct);
        }
        catch (A11yException error)
        {
            Assert.Equal($"node {entry.Id} does not support taking focus", error.Message);
        }

        var baseline = Volatile.Read(ref events);
        var waiting = service.WaitTextAsync(window, "periwinkle", TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(300, Ct);
        Assert.False(waiting.IsCompleted);
        await service.SetTextAsync(entry.Id, "periwinkle", Ct);
        var seen = await waiting;
        Assert.NotNull(seen);
        Assert.Equal(entry.Id, seen.Id);
        Assert.Equal("periwinkle", seen.Text);
        Assert.True(Volatile.Read(ref events) > baseline, "no accessibility event arrived after the text changed");

        var ok = Assert.Single(await service.FindAsync(window, "push-button", "ok", [], 5, Ct));
        await service.DoActionAsync(ok.Id, null, Ct);
        await zenity.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(0, zenity.ExitCode);
        Assert.Equal("periwinkle", environment.Output(zenity).Trim());
    }

    [Fact]
    public async Task A_bad_node_id_is_a_sentence_not_a_bus_error()
    {
        var missing = LiveEnvironment.Missing();
        Assert.SkipWhen(missing is not null, missing ?? string.Empty);
        var (environment, bus) = await StartAsync();
        await using var _ = environment;
        await using var __ = bus;
        var service = new A11yService(bus);

        var malformed = await Assert.ThrowsAsync<A11yException>(() => service.DoActionAsync("button 7", "click", Ct));
        Assert.Contains("is not a node id", malformed.Message, StringComparison.Ordinal);

        var noOwner = await Assert.ThrowsAsync<A11yException>(
            () => service.GrabFocusAsync(":1.9999:/org/a11y/atspi/accessible/1", Ct));
        Assert.Contains("is gone", noOwner.Message, StringComparison.Ordinal);

        var registryChild = await Assert.ThrowsAsync<A11yException>(
            () => service.ActionsAsync("org.a11y.atspi.Registry:/org/a11y/atspi/accessible/nothing", Ct));
        Assert.Contains("no longer exists", registryChild.Message, StringComparison.Ordinal);
    }
}
