using System.Net;
using System.Net.Sockets;
using Basin;
using Basin.Capabilities;
using Basin.Transport.Waypipe;
using Wayland.Server;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

internal sealed class WaypipeAcceptor : IChannelOwner, IDisposable
{
    private readonly ISessionHost _host;
    private readonly WaypipeCompression _compression;
    private readonly bool _gpu;
    private readonly string? _video;
    private readonly IVideoDecoder? _decoder;
    private readonly List<WaypipeChannel> _channels = [];
    private Socket? _listener;
    private LinuxDmabufGlobal? _dmabuf;
    private int _attached;
    private bool _disposed;

    public WaypipeAcceptor(
        string name,
        ISessionHost host,
        WaypipeCompression compression,
        bool gpu,
        string? video,
        IVideoDecoder? decoder,
        SessionSettings? session)
    {
        Name = name;
        _host = host;
        _compression = compression;
        _gpu = gpu;
        _video = video;
        _decoder = decoder;
        Session = session;
        Globals = new WaypipeGlobals(gpu, video is not null && decoder is not null);
    }

    public string Name { get; }

    public SessionSettings? Session { get; }

    public WaypipeGlobals Globals { get; }

    public int Attached => Volatile.Read(ref _attached);

    public int Live
    {
        get
        {
            lock (_channels)
            {
                return _channels.Count;
            }
        }
    }

    public event Action? Changed;

    public event Action<Exception>? Failed;

    public bool OwnsDmabuf(WlGlobal global) => _dmabuf is { } dmabuf && dmabuf.Owns(global);

    public static EndPoint? ParseEndpoint(string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        error = null;
        if (text.Contains(':', StringComparison.Ordinal))
        {
            var parsed = IPEndPoint.Parse(text);
            if (parsed.Address.Equals(IPAddress.Any) || parsed.Address.Equals(IPAddress.IPv6Any))
            {
                error = "a waypipe channel binds an explicit address, never a wildcard";
                return null;
            }

            return parsed;
        }

        if (File.Exists(text))
        {
            File.Delete(text);
        }

        return new UnixDomainSocketEndPoint(text);
    }

    public static Socket Listen(EndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var listener = new Socket(
            endpoint.AddressFamily,
            SocketType.Stream,
            endpoint is UnixDomainSocketEndPoint ? ProtocolType.Unspecified : ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(8);
        return listener;
    }

    public void Accept(Socket listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listener = listener;
        _ = AcceptLoopAsync(listener);
    }

    private async Task AcceptLoopAsync(Socket listener)
    {
        try
        {
            while (true)
            {
                var accepted = await listener.AcceptAsync();
                Adopt(new NetworkStream(accepted, ownsSocket: true));
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    public void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_disposed && !_host.ShuttingDown)
        {
            Log.Error($"{Name}: the channel listener failed: {error.Message}");
            Failed?.Invoke(error);
        }
    }

    public void Adopt(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (_disposed)
        {
            stream.Dispose();
            return;
        }

        var channel = WaypipeChannel.AttachChannel(
            stream,
            _compression,
            options: new WaypipeChannelOptions
            {
                CarriesDmabuf = _gpu,
                AcceptsVideo = _video is not null,
                VideoDecoder = _decoder,
            });
        int index;
        lock (_channels)
        {
            _channels.Add(channel);
            index = ++_attached;
        }

        channel.Ended += failure =>
        {
            lock (_channels)
            {
                _channels.Remove(channel);
            }

            if (failure is null)
            {
                Log.Debug($"{Name}: channel {index} ended");
                _host.Status($"{Name}: channel {index} ended");
            }
            else
            {
                Log.Warn($"{Name}: channel {index} ended: {failure.Message}");
                _host.Status($"{Name}: channel {index} ended: {failure.Message}");
            }

            Changed?.Invoke();
        };
        var formats = channel.Globals.Formats;
        _host.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var compositor = _host.Compositor;
            if (_gpu && _dmabuf is null)
            {
                _dmabuf = new LinuxDmabufGlobal(
                    compositor.Display,
                    compositor.Services.Require<ClientBufferRegistry>(),
                    formats,
                    WaypipeGlobals.SyntheticMainDevice,
                    compositor: compositor.Services.Require<CompositorGlobal>());
            }

            var client = compositor.Display.CreateClient(channel.Transport);
            _host.Attach(this, client);
        });
        _host.Status($"{Name}: {index} channel client(s) attached");
        Changed?.Invoke();
    }

    public void CloseChannels()
    {
        WaypipeChannel[] open;
        lock (_channels)
        {
            open = [.. _channels];
            _channels.Clear();
        }

        foreach (var channel in open)
        {
            channel.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Dispose();
        CloseChannels();
        if (_dmabuf is { } dmabuf)
        {
            _host.Post(dmabuf.Dispose);
        }
    }
}
