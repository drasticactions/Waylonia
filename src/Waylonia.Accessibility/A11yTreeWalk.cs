using System.Collections.Concurrent;
using Tmds.DBus.Protocol;
using AccessibleProxy = Waylonia.Accessibility.DBus.Accessible;
using CacheProxy = Waylonia.Accessibility.DBus.Cache;

namespace Waylonia.Accessibility;

internal sealed class A11yTreeWalk
{
    private readonly DBusConnection _connection;
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private readonly CancellationToken _cancel;
    private readonly ConcurrentDictionary<string, Task<BusCache?>> _caches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<A11yVisit> _visits = [];
    private bool _capHit;
    private int _depthCut;

    public A11yTreeWalk(DBusConnection connection, int maxDepth, int maxNodes, CancellationToken cancel)
    {
        _connection = connection;
        _maxDepth = Math.Max(0, maxDepth);
        _maxNodes = Math.Max(1, maxNodes);
        _cancel = cancel;
    }

    public async Task<A11yWalkResult> RunAsync(A11yNodeRef root)
    {
        var node = await InfoAsync(root).ConfigureAwait(false) ??
            throw new A11yException($"node {root.Id} no longer exists");
        await VisitAsync(node, 0).ConfigureAwait(false);
        return new A11yWalkResult(_visits, _capHit, _depthCut);
    }

    private async Task VisitAsync(A11yNode node, int depth)
    {
        _cancel.ThrowIfCancellationRequested();
        if (_visits.Count >= _maxNodes)
        {
            _capHit = true;
            return;
        }

        if (!_seen.Add(node.Ref.Id))
        {
            return;
        }

        _visits.Add(new A11yVisit(node, depth));
        if (node.ChildCount == 0)
        {
            return;
        }

        if (depth >= _maxDepth)
        {
            if (await HasChildrenAsync(node).ConfigureAwait(false))
            {
                _depthCut++;
            }

            return;
        }

        var children = await ChildrenAsync(node).ConfigureAwait(false);
        var budget = _maxNodes - _visits.Count;
        if (children.Count > budget)
        {
            _capHit = true;
        }

        var infos = await Task.WhenAll(children.Take(Math.Max(0, budget)).Select(InfoAsync)).ConfigureAwait(false);
        foreach (var child in infos)
        {
            if (child is not null)
            {
                await VisitAsync(child, depth + 1).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> HasChildrenAsync(A11yNode node)
    {
        if (node.ChildCount > 0)
        {
            return true;
        }

        var count = await A11yCalls.TryRun(Accessible(node.Ref).GetChildCountAsync(), _cancel).ConfigureAwait(false);
        return count > 0;
    }

    public async Task<IReadOnlyList<A11yNodeRef>> ChildrenAsync(A11yNode node)
    {
        var cache = await CacheAsync(node.Ref.Bus).ConfigureAwait(false);
        if (cache is not null &&
            cache.Items.TryGetValue(node.Ref.Path, out var item) &&
            cache.Children.TryGetValue(node.Ref.Path, out var cached) &&
            cached.Count == item.ChildCount)
        {
            return cached;
        }

        var children = await A11yCalls.TryRun(Accessible(node.Ref).GetChildrenAsync(), _cancel).ConfigureAwait(false);
        if (children is null)
        {
            return [];
        }

        var refs = new List<A11yNodeRef>(children.Length);
        foreach (var (bus, path) in children)
        {
            var child = new A11yNodeRef(bus, path.ToString());
            if (!child.IsNull)
            {
                refs.Add(child);
            }
        }

        return refs;
    }

    public async Task<A11yNode?> InfoAsync(A11yNodeRef node)
    {
        var cache = await CacheAsync(node.Bus).ConfigureAwait(false);
        if (cache is not null && cache.Items.TryGetValue(node.Path, out var item))
        {
            return new A11yNode(node, item.Role, item.Name, item.States, item.Interfaces, item.ChildCount);
        }

        return await FetchAsync(_connection, node, _cancel).ConfigureAwait(false);
    }

    public static async Task<A11yNode?> FetchAsync(DBusConnection connection, A11yNodeRef node, CancellationToken cancel)
    {
        var proxy = new AccessibleProxy(connection, node.Bus, node.Path);
        var role = proxy.GetRoleAsync();
        var name = proxy.GetNameAsync();
        var states = proxy.GetStateAsync();
        var interfaces = proxy.GetInterfacesAsync();
        try
        {
            await Task.WhenAll(role, name, states, interfaces).WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return null;
        }
        catch (Exception error) when (error is DBusConnectionClosedException or DBusConnectionException)
        {
            throw A11yCalls.Closed();
        }

        return new A11yNode(node, role.Result, name.Result ?? string.Empty, states.Result, interfaces.Result, -1);
    }

    private AccessibleProxy Accessible(A11yNodeRef node) => new(_connection, node.Bus, node.Path);

    private Task<BusCache?> CacheAsync(string bus) => _caches.GetOrAdd(bus, LoadCacheAsync);

    private async Task<BusCache?> LoadCacheAsync(string bus)
    {
        var items = await A11yCalls.TryRun(
            new CacheProxy(_connection, bus, A11yBus.CachePath).GetItemsAsync(), _cancel).ConfigureAwait(false);
        if (items is null || items.Length == 0)
        {
            return null;
        }

        var byPath = new Dictionary<string, CachedItem>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<(int Index, A11yNodeRef Node)>>(StringComparer.Ordinal);
        foreach (var (self, _, parent, index, childCount, interfaces, name, role, _, states) in items)
        {
            var path = self.Item2.ToString();
            byPath[path] = new CachedItem(role, name ?? string.Empty, states, interfaces, childCount);
            var parentPath = parent.Item2.ToString();
            if (parent.Item1 == bus && parentPath != A11yNodeRef.NullPath)
            {
                if (!children.TryGetValue(parentPath, out var list))
                {
                    children[parentPath] = list = [];
                }

                list.Add((index, new A11yNodeRef(self.Item1, path)));
            }
        }

        var ordered = new Dictionary<string, IReadOnlyList<A11yNodeRef>>(StringComparer.Ordinal);
        foreach (var (parent, list) in children)
        {
            ordered[parent] = list.OrderBy(entry => entry.Index).Select(entry => entry.Node).ToList();
        }

        return new BusCache(byPath, ordered);
    }

    private sealed record CachedItem(uint Role, string Name, uint[] States, string[] Interfaces, int ChildCount);

    private sealed record BusCache(
        IReadOnlyDictionary<string, CachedItem> Items,
        IReadOnlyDictionary<string, IReadOnlyList<A11yNodeRef>> Children);
}
