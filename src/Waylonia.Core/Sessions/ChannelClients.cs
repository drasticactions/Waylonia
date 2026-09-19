using System.Collections.Concurrent;
using Basin.Transport.Waypipe;
using Wayland.Server;

namespace Waylonia.Sessions;

internal sealed class ChannelClients
{
    private const string Dmabuf = "zwp_linux_dmabuf_v1";

    private readonly ConcurrentDictionary<WlClient, IChannelOwner> _clients = new();
    private readonly HashSet<IChannelOwner> _owners = [];

    public int Count => _clients.Count;

    public event Action<WlClient>? Removed;

    public void Add(WlClient client, IChannelOwner owner)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(owner);
        _clients[client] = owner;
        _owners.Add(owner);
        client.Destroyed += () =>
        {
            _clients.TryRemove(client, out _);
            Removed?.Invoke(client);
        };
    }

    public IChannelOwner? OwnerOf(WlClient client) => _clients.GetValueOrDefault(client);

    public bool Filter(WlClient client, WlGlobal? global, string interfaceName)
    {
        var owner = OwnerOf(client);
        if (global is not null && interfaceName == Dmabuf)
        {
            if (owner is not null)
            {
                return owner.OwnsDmabuf(global);
            }

            foreach (var channelOwner in _owners)
            {
                if (channelOwner.OwnsDmabuf(global))
                {
                    return false;
                }
            }

            return true;
        }

        return owner is null || owner.Globals.Carries(interfaceName);
    }
}
