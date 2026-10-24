using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
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
    private IDisposable? _listener;
    private IWaypipeListener? _waypipeListener;
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
        SshSession? session)
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

    public SshSession? Session { get; }

    public WaypipeGlobals Globals { get; }

    public bool Carries(string interfaceName) => Globals.Carries(interfaceName);

    public bool SeesDmabuf(WlGlobal global) => ExclusiveDmabuf(global);

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

    public bool ExclusiveDmabuf(WlGlobal global) => _dmabuf is { } dmabuf && dmabuf.Owns(global);

    [UnsupportedOSPlatform("browser")]
    public static EndPoint? ParseEndpoint(string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        error = null;
        if (text.Contains(':', StringComparison.Ordinal))
        {
            var parsed = IPEndPoint.Parse(text);
            if (parsed.Address.Equals(IPAddress.Any) || parsed.Address.Equals(IPAddress.IPv6Any))
            {
                error = "A waypipe listener needs an explicit address, not a wildcard such as 0.0.0.0.";
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

    [UnsupportedOSPlatform("browser")]
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

    public WaypipeChannelOptions ChannelOptions => new()
    {
        CarriesDmabuf = _gpu,
        AcceptsVideo = _video is not null,
        VideoDecoder = _decoder,
    };

    public WaypipeCompression Compression => _compression;

    public void Accept(IWaypipeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (_disposed)
        {
            listener.Dispose();
            return;
        }

        _waypipeListener = listener;
        listener.ChannelAccepted += Adopt;
        listener.Failed += Fail;
    }

    [UnsupportedOSPlatform("browser")]
    public void Accept(Socket listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listener = listener;
        _ = AcceptLoopAsync(listener);
    }

    [UnsupportedOSPlatform("browser")]
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
        catch (ObjectDisposedException error)
        {
            Log.Debug(error, $"session {Name}: the channel listener closed");
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
            Log.Error(error, $"session {Name}: the channel listener failed");
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

        Adopt(WaypipeChannel.AttachChannel(stream, _compression, options: ChannelOptions));
    }

    public void Adopt(WaypipeChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_disposed)
        {
            channel.Dispose();
            return;
        }

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
                Log.Debug($"session {Name}: channel {index} ended");
                _host.Status($"{Name}: Channel {index} ended");
            }
            else
            {
                Log.Warn(failure, $"session {Name}: channel {index} ended with an error");
                _host.Status($"{Name}: Channel {index} ended with an error");
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
        _host.Status($"{Name}: Channel {index} attached");
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
        if (_waypipeListener is { } waypipeListener)
        {
            _host.Post(waypipeListener.Dispose);
        }

        CloseChannels();
        if (_dmabuf is { } dmabuf)
        {
            _host.Post(dmabuf.Dispose);
        }
    }
}
