using Wayland.Server;

namespace Waylonia.Sessions;

internal sealed class DirectOwner(string name, SshSession? session, Func<WlGlobal, bool> hostDmabuf) : IChannelOwner
{
    private readonly List<WlClient> _clients = [];
    private int _attached;

    public string Name => name;

    public SshSession? Session => session;

    public int Attached => Volatile.Read(ref _attached);

    public bool Carries(string interfaceName) => true;

    public bool ExclusiveDmabuf(WlGlobal global) => false;

    public bool SeesDmabuf(WlGlobal global) => hostDmabuf(global);

    public void Adopted(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Interlocked.Increment(ref _attached);
        lock (_clients)
        {
            _clients.Add(client);
        }

        client.Destroyed += () =>
        {
            lock (_clients)
            {
                _clients.Remove(client);
            }
        };
    }

    public void CloseClients()
    {
        WlClient[] open;
        lock (_clients)
        {
            open = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in open)
        {
            client.Destroy();
        }
    }
}
