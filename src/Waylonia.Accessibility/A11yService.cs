using Tmds.DBus.Protocol;
using AccessibleProxy = Waylonia.Accessibility.DBus.Accessible;
using ActionProxy = Waylonia.Accessibility.DBus.Action;
using ComponentProxy = Waylonia.Accessibility.DBus.Component;
using EditableTextProxy = Waylonia.Accessibility.DBus.EditableText;
using TextProxy = Waylonia.Accessibility.DBus.Text;
using ValueProxy = Waylonia.Accessibility.DBus.Value;

namespace Waylonia.Accessibility;

/// <summary>
/// What an agent asks of the accessibility bus: applications, the window that belongs to a Wayland toplevel, its
/// tree, searches, and the actions, text, values and focus of single nodes. Every member is safe to call from any
/// thread and concurrently.
/// </summary>
public sealed class A11yService
{
    /// <summary>
    /// The coordinate type AT-SPI calls <c>ATSPI_COORD_TYPE_WINDOW</c>.
    /// </summary>
    public const uint WindowCoordinates = 1;

    /// <summary>
    /// How many characters of Text content a node reports.
    /// </summary>
    public const int TextLimit = 200;

    private const int SearchDepth = 64;
    private const int SearchNodes = 5000;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly A11yBus _bus;

    /// <summary>
    /// Creates the service over a connected bus.
    /// </summary>
    /// <param name="bus">The accessibility bus.</param>
    public A11yService(A11yBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
    }

    private DBusConnection Connection => _bus.Connection;

    /// <summary>
    /// The applications on the accessibility bus: the registry root's children, or every bus name that exports an
    /// application root when no registry answers, each with the process id behind its bus name.
    /// </summary>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>Bus name, pid (0 when unknown) and accessible name of each application.</returns>
    public async Task<IReadOnlyList<(string BusName, int Pid, string Name)>> ApplicationsAsync(CancellationToken cancel) =>
        (await ApplicationListAsync(cancel).ConfigureAwait(false))
            .Select(app => (app.Root.Bus, app.Pid, app.Name))
            .ToList();

    private async Task<IReadOnlyList<A11yApplication>> ApplicationListAsync(CancellationToken cancel)
    {
        var registryRoot = new A11yNodeRef(A11yBus.RegistryName, A11yBus.RootPath);
        IEnumerable<A11yNodeRef> roots;
        var children = await TryAsync(new AccessibleProxy(Connection, registryRoot.Bus, registryRoot.Path).GetChildrenAsync(), cancel)
            .ConfigureAwait(false);
        if (children is not null)
        {
            roots = children.Select(child => new A11yNodeRef(child.Item1, child.Item2.ToString())).Where(root => !root.IsNull);
        }
        else
        {
            roots = await ExportedRootsAsync(cancel).ConfigureAwait(false);
        }

        var apps = await Task.WhenAll(roots.Select(root => ApplicationAsync(root, cancel))).ConfigureAwait(false);
        return apps.OfType<A11yApplication>().ToList();
    }

    private async Task<IEnumerable<A11yNodeRef>> ExportedRootsAsync(CancellationToken cancel)
    {
        string[] names;
        try
        {
            names = await Connection.ListServicesAsync().WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return [];
        }

        var own = Connection.UniqueName;
        var probes = names
            .Where(name => name.StartsWith(':') && name != own)
            .Select(async name =>
            {
                var role = await TryAsync(new AccessibleProxy(Connection, name, A11yBus.RootPath).GetRoleAsync(), cancel)
                    .ConfigureAwait(false);
                return role == (uint)AtSpiRole.Application ? new A11yNodeRef(name, A11yBus.RootPath) : (A11yNodeRef?)null;
            });
        var found = await Task.WhenAll(probes).ConfigureAwait(false);
        return found.OfType<A11yNodeRef>();
    }

    private async Task<A11yApplication?> ApplicationAsync(A11yNodeRef root, CancellationToken cancel)
    {
        var name = TryAsync(new AccessibleProxy(Connection, root.Bus, root.Path).GetNameAsync(), cancel);
        var pid = TryAsync(BusDaemon.ProcessIdAsync(Connection, root.Bus), cancel);
        await Task.WhenAll(name, pid).ConfigureAwait(false);
        return name.Result is null ? null : new A11yApplication(root, (int)pid.Result, name.Result);
    }

    /// <summary>
    /// Pairs a Wayland toplevel with an application's top-level frame, window, dialog or alert node: the same pid
    /// first, then the same title, then, among several of those, the one whose size is closest to the window's client
    /// or surface size, preferring showing nodes throughout. When no application has the pid, a unique title match
    /// on any application is taken, which covers a client behind a proxy. The offset comes from
    /// <see cref="A11yOffsets.For"/>.
    /// </summary>
    /// <param name="target">The Wayland window.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>The pairing, or null when nothing matches.</returns>
    public async Task<A11yWindow?> FindWindowAsync(A11yWindowTarget target, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(target);
        var apps = await ApplicationListAsync(cancel).ConfigureAwait(false);
        var owners = apps.Where(app => app.Pid == target.Pid && target.Pid > 0).ToList();
        var byPid = owners.Count > 0;
        if (!byPid)
        {
            if (string.IsNullOrEmpty(target.Title))
            {
                return null;
            }

            owners = apps.ToList();
        }

        var candidates = (await Task.WhenAll(owners.Select(app => TopLevelsAsync(app, cancel))).ConfigureAwait(false))
            .SelectMany(list => list)
            .ToList();
        var pool = candidates;
        if (!string.IsNullOrEmpty(target.Title))
        {
            var titled = candidates.Where(c => string.Equals(c.Node.Name, target.Title, StringComparison.Ordinal)).ToList();
            if (titled.Count > 0)
            {
                pool = titled;
            }
            else if (!byPid)
            {
                return null;
            }
        }

        var showing = pool.Where(c => A11yNames.Has(c.Node.States, AtSpiState.Showing)).ToList();
        if (showing.Count > 0)
        {
            pool = showing;
        }

        if (pool.Count == 0 || (!byPid && pool.Count > 1))
        {
            return null;
        }

        var extents = await Task.WhenAll(pool.Select(c => RawExtentsAsync(c.Node, cancel))).ConfigureAwait(false);
        var best = 0;
        if (pool.Count > 1)
        {
            var bestDistance = int.MaxValue;
            for (var i = 0; i < pool.Count; i++)
            {
                if (extents[i] is not { } box)
                {
                    continue;
                }

                var distance = Math.Min(
                    A11yOffsets.Distance(box, target.ClientWidth, target.ClientHeight),
                    A11yOffsets.Distance(box, target.SurfaceWidth, target.SurfaceHeight));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
        }

        var chosen = pool[best];
        var offset = extents[best] is { } rootBox ? A11yOffsets.For(rootBox, target) : (0, 0);
        return new A11yWindow(chosen.Node.Ref.Id, chosen.App.Name, chosen.App.Pid, offset.Item1, offset.Item2);
    }

    private async Task<IReadOnlyList<(A11yApplication App, A11yNode Node)>> TopLevelsAsync(
        A11yApplication app,
        CancellationToken cancel)
    {
        var children = await TryAsync(new AccessibleProxy(Connection, app.Root.Bus, app.Root.Path).GetChildrenAsync(), cancel)
            .ConfigureAwait(false);
        if (children is null)
        {
            return [];
        }

        var nodes = await Task.WhenAll(children
            .Select(child => new A11yNodeRef(child.Item1, child.Item2.ToString()))
            .Where(child => !child.IsNull)
            .Select(child => A11yTreeWalk.FetchAsync(Connection, child, cancel))).ConfigureAwait(false);
        return nodes
            .OfType<A11yNode>()
            .Where(node => A11yNames.IsWindowRole(node.Role))
            .Select(node => (app, node))
            .ToList();
    }

    /// <summary>
    /// A window's tree as text, one <see cref="A11yTreeText.Line"/> per node in document order, children indented two
    /// spaces per depth, followed by a line for each limit that cut the walk.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="maxDepth">The deepest level listed; the root is level 0.</param>
    /// <param name="maxNodes">The most nodes listed.</param>
    /// <param name="cancel">Cancels the walk.</param>
    /// <returns>The text.</returns>
    public async Task<string> TreeTextAsync(A11yWindow window, int maxDepth, int maxNodes, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(window);
        var walk = await WalkAsync(window, maxDepth, maxNodes, cancel).ConfigureAwait(false);
        var boxes = await Task.WhenAll(walk.Nodes.Select(visit => BoxAsync(window, visit.Node, cancel))).ConfigureAwait(false);
        var lines = new List<string>(walk.Nodes.Count + 2);
        for (var i = 0; i < walk.Nodes.Count; i++)
        {
            var node = walk.Nodes[i].Node;
            var info = new A11yNodeInfo(node.Ref.Id, node.RoleName, node.Name, node.StateNames, boxes[i], null);
            lines.Add(A11yTreeText.Line(info, walk.Nodes[i].Depth));
        }

        if (walk.NodeCapHit)
        {
            lines.Add(A11yTreeText.NodeCapLine(maxNodes));
        }

        if (walk.DepthCut > 0)
        {
            lines.Add(A11yTreeText.DepthCapLine(maxDepth, walk.DepthCut));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The nodes under a window that match a role, a case-insensitive name substring and a set of states, in document
    /// order.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="role">A role name such as <c>push-button</c>, or null for any role.</param>
    /// <param name="name">A substring of the name, or null for any name.</param>
    /// <param name="states">States every match must hold, such as <c>showing</c>.</param>
    /// <param name="max">The most matches returned.</param>
    /// <param name="cancel">Cancels the search.</param>
    /// <returns>The matches with their boxes and text.</returns>
    public async Task<IReadOnlyList<A11yNodeInfo>> FindAsync(
        A11yWindow window,
        string? role,
        string? name,
        IReadOnlyList<string> states,
        int max,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(window);
        var wantedStates = (states ?? []).Select(A11yNames.Normalize).ToList();
        var walk = await WalkAsync(window, SearchDepth, SearchNodes, cancel).ConfigureAwait(false);
        var matches = walk.Nodes
            .Select(visit => visit.Node)
            .Where(node => string.IsNullOrWhiteSpace(role) || A11yNames.RoleMatches(role, node.Role))
            .Where(node => string.IsNullOrEmpty(name) || node.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Where(node =>
            {
                var held = node.StateNames;
                return wantedStates.All(state => held.Contains(state, StringComparer.Ordinal));
            })
            .Take(Math.Max(0, max))
            .ToList();
        return await Task.WhenAll(matches.Select(node => DescribeAsync(window, node, cancel))).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until a node under the window has a name or Text content that contains a string, case ignored. The tree
    /// is searched once at the start and again after each burst of accessibility events, never on a timer.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="text">The string to find.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancel">Cancels the wait.</param>
    /// <returns>The first matching node, or null when the timeout passed first.</returns>
    public async Task<A11yNodeInfo?> WaitTextAsync(A11yWindow window, string text, TimeSpan timeout, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrEmpty(text);
        var deadline = DateTime.UtcNow + timeout;
        var pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEvent() => Volatile.Read(ref pulse).TrySetResult();
        _bus.EventReceived += OnEvent;
        try
        {
            while (true)
            {
                Volatile.Write(ref pulse, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                var waitFor = Volatile.Read(ref pulse).Task;
                var hit = await SearchTextAsync(window, text, cancel).ConfigureAwait(false);
                if (hit is not null)
                {
                    return hit;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return null;
                }

                var delay = Task.Delay(remaining, cancel);
                if (await Task.WhenAny(waitFor, delay).ConfigureAwait(false) != waitFor)
                {
                    cancel.ThrowIfCancellationRequested();
                    return null;
                }

                var quiet = deadline - DateTime.UtcNow;
                if (quiet > Debounce)
                {
                    quiet = Debounce;
                }

                if (quiet > TimeSpan.Zero)
                {
                    await Task.Delay(quiet, cancel).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _bus.EventReceived -= OnEvent;
        }
    }

    private async Task<A11yNodeInfo?> SearchTextAsync(A11yWindow window, string text, CancellationToken cancel)
    {
        A11yWalkResult walk;
        try
        {
            walk = await WalkAsync(window, SearchDepth, SearchNodes, cancel).ConfigureAwait(false);
        }
        catch (A11yException)
        {
            return null;
        }

        var contents = await Task.WhenAll(walk.Nodes.Select(visit => visit.Node.Implements(A11yNode.TextInterface)
            ? FullTextAsync(visit.Node.Ref, cancel)
            : Task.FromResult<string?>(null))).ConfigureAwait(false);
        for (var i = 0; i < walk.Nodes.Count; i++)
        {
            var node = walk.Nodes[i].Node;
            if (node.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                (contents[i]?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return await DescribeAsync(window, node, cancel).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>
    /// The names of a node's actions, in index order.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>The names, such as <c>click</c>.</returns>
    public async Task<IReadOnlyList<string>> ActionsAsync(string nodeId, CancellationToken cancel)
    {
        var node = A11yNodeRef.Parse(nodeId);
        await RequireAsync(node, A11yNode.ActionInterface, "has no actions", cancel).ConfigureAwait(false);
        return await ActionNamesAsync(node, cancel).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ActionNamesAsync(A11yNodeRef node, CancellationToken cancel)
    {
        var proxy = new ActionProxy(Connection, node.Bus, node.Path);
        var count = await A11yCalls.Run(proxy.GetNActionsAsync(), node, "actions", cancel).ConfigureAwait(false);
        var names = await Task.WhenAll(Enumerable.Range(0, Math.Max(0, count))
            .Select(index => A11yCalls.Run(proxy.GetNameAsync(index), node, "actions", cancel))).ConfigureAwait(false);
        return names;
    }

    /// <summary>
    /// Performs one of a node's actions, by name with case ignored, or the first one when the name is null.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="action">The action name, such as <c>click</c>, <c>activate</c>, <c>press</c> or <c>toggle</c>.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>A task that completes once the application accepted the action.</returns>
    public async Task DoActionAsync(string nodeId, string? action, CancellationToken cancel)
    {
        var node = A11yNodeRef.Parse(nodeId);
        await RequireAsync(node, A11yNode.ActionInterface, "has no actions", cancel).ConfigureAwait(false);
        var names = await ActionNamesAsync(node, cancel).ConfigureAwait(false);
        if (names.Count == 0)
        {
            throw new A11yException($"node {node.Id} has no actions");
        }

        var index = 0;
        if (action is not null)
        {
            index = -1;
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], action, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw new A11yException(
                    $"node {node.Id} has no action \"{action}\"; its actions are {string.Join(", ", names.Select(n => $"\"{n}\""))}");
            }
        }

        var done = await A11yCalls.Run(
            new ActionProxy(Connection, node.Bus, node.Path).DoActionAsync(index), node, $"the action \"{names[index]}\"", cancel)
            .ConfigureAwait(false);
        if (!done)
        {
            throw new A11yException($"node {node.Id} did not perform \"{names[index]}\"");
        }
    }

    /// <summary>
    /// Replaces a node's whole text through <c>EditableText.SetTextContents</c>.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="text">The new text.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>A task that completes once the application took the text.</returns>
    public async Task SetTextAsync(string nodeId, string text, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(text);
        var node = A11yNodeRef.Parse(nodeId);
        await RequireAsync(node, A11yNode.EditableTextInterface, "is not editable text", cancel).ConfigureAwait(false);
        var done = await A11yCalls.Run(
            new EditableTextProxy(Connection, node.Bus, node.Path).SetTextContentsAsync(text), node, "setting its text", cancel)
            .ConfigureAwait(false);
        if (!done)
        {
            throw new A11yException($"node {node.Id} refused the new text; it may be read-only or not showing");
        }
    }

    /// <summary>
    /// Sets a node's <c>Value.CurrentValue</c>.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>A task that completes once the property was set.</returns>
    public async Task SetValueAsync(string nodeId, double value, CancellationToken cancel)
    {
        var node = A11yNodeRef.Parse(nodeId);
        await RequireAsync(node, A11yNode.ValueInterface, "has no value to set", cancel).ConfigureAwait(false);
        await A11yCalls.Run(
            new ValueProxy(Connection, node.Bus, node.Path).SetCurrentValueAsync(value), node, "setting its value", cancel)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the keyboard focus to a node through <c>Component.GrabFocus</c>.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>A task that completes once the application took focus.</returns>
    public async Task GrabFocusAsync(string nodeId, CancellationToken cancel)
    {
        var node = A11yNodeRef.Parse(nodeId);
        await RequireAsync(node, A11yNode.ComponentInterface, "cannot take focus", cancel).ConfigureAwait(false);
        var done = await A11yCalls.Run(
            new ComponentProxy(Connection, node.Bus, node.Path).GrabFocusAsync(), node, "taking focus", cancel)
            .ConfigureAwait(false);
        if (!done)
        {
            throw new A11yException($"node {node.Id} refused focus; it may not be focusable or not showing");
        }
    }

    /// <summary>
    /// A node's extents relative to the window's client area.
    /// </summary>
    /// <param name="window">The window the node belongs to.</param>
    /// <param name="nodeId">The node id.</param>
    /// <param name="cancel">Cancels the request.</param>
    /// <returns>The box, or null when the node has no Component interface or no extents.</returns>
    public async Task<A11yBox?> ExtentsAsync(A11yWindow window, string nodeId, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(window);
        var node = A11yNodeRef.Parse(nodeId);
        var interfaces = await InterfacesAsync(node, cancel).ConfigureAwait(false);
        if (Array.IndexOf(interfaces, A11yNode.ComponentInterface) < 0)
        {
            return null;
        }

        var (x, y, width, height) = await A11yCalls.Run(
            new ComponentProxy(Connection, node.Bus, node.Path).GetExtentsAsync(WindowCoordinates), node, "extents", cancel)
            .ConfigureAwait(false);
        return Relative(window, x, y, width, height);
    }

    private Task<A11yWalkResult> WalkAsync(A11yWindow window, int maxDepth, int maxNodes, CancellationToken cancel) =>
        new A11yTreeWalk(Connection, maxDepth, maxNodes, cancel).RunAsync(A11yNodeRef.Parse(window.RootId));

    private async Task<A11yNodeInfo> DescribeAsync(A11yWindow window, A11yNode node, CancellationToken cancel)
    {
        var box = BoxAsync(window, node, cancel);
        var text = node.Implements(A11yNode.TextInterface) ? FullTextAsync(node.Ref, cancel) : Task.FromResult<string?>(null);
        await Task.WhenAll(box, text).ConfigureAwait(false);
        return new A11yNodeInfo(
            node.Ref.Id,
            node.RoleName,
            node.Name,
            node.StateNames,
            box.Result,
            text.Result is { } content ? A11yTreeText.Truncate(content, TextLimit) : null);
    }

    private async Task<A11yBox?> BoxAsync(A11yWindow window, A11yNode node, CancellationToken cancel)
    {
        if (!node.Implements(A11yNode.ComponentInterface))
        {
            return null;
        }

        var extents = await TryAsync(
            new ComponentProxy(Connection, node.Ref.Bus, node.Ref.Path).GetExtentsAsync(WindowCoordinates), cancel)
            .ConfigureAwait(false);
        return extents is var (x, y, width, height) && extents != default ? Relative(window, x, y, width, height) : null;
    }

    private async Task<A11yBox?> RawExtentsAsync(A11yNode node, CancellationToken cancel)
    {
        if (!node.Implements(A11yNode.ComponentInterface))
        {
            return null;
        }

        var extents = await TryAsync(
            new ComponentProxy(Connection, node.Ref.Bus, node.Ref.Path).GetExtentsAsync(WindowCoordinates), cancel)
            .ConfigureAwait(false);
        return extents is var (x, y, width, height) && extents != default && A11yOffsets.IsPlausible(x, y, width, height)
            ? new A11yBox(x, y, width, height)
            : null;
    }

    private static A11yBox? Relative(A11yWindow window, int x, int y, int width, int height) =>
        A11yOffsets.IsPlausible(x, y, width, height)
            ? new A11yBox(x - window.OffsetX, y - window.OffsetY, width, height)
            : null;

    private async Task<string?> FullTextAsync(A11yNodeRef node, CancellationToken cancel) =>
        await TryAsync(new TextProxy(Connection, node.Bus, node.Path).GetTextAsync(0, -1), cancel).ConfigureAwait(false);

    private async Task<string[]> InterfacesAsync(A11yNodeRef node, CancellationToken cancel) =>
        await A11yCalls.Run(
            new AccessibleProxy(Connection, node.Bus, node.Path).GetInterfacesAsync(),
            node,
            "the Accessible interface",
            cancel,
            unsupportedMeansGone: true)
            .ConfigureAwait(false);

    private async Task RequireAsync(A11yNodeRef node, string @interface, string missing, CancellationToken cancel)
    {
        var interfaces = await InterfacesAsync(node, cancel).ConfigureAwait(false);
        if (Array.IndexOf(interfaces, @interface) < 0)
        {
            throw new A11yException($"node {node.Id} {missing}");
        }
    }

    private static Task<T?> TryAsync<T>(Task<T> call, CancellationToken cancel) => A11yCalls.TryRun(call, cancel);
}
