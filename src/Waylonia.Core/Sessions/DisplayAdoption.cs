using System.Runtime.InteropServices;
using Basin;
using Wayland;
using Wayland.Server;

namespace Waylonia.Sessions;

internal sealed class DisplayAdoption : IDisposable
{
    private const int SockCloexec = 0x80000;
    private const int DuplicateCloexec = 1030;
    private const int GetStatusFlags = 3;
    private const int SetStatusFlags = 4;
    private const int NonBlocking = 0x800;

    private readonly WlServerDisplay _display;
    private readonly int _fd;
    private readonly Action<WlClient> _adopted;
    private IEventSource? _source;

    private DisplayAdoption(WlServerDisplay display, int fd, Action<WlClient> adopted)
    {
        _display = display;
        _fd = fd;
        _adopted = adopted;
    }

    public static int Duplicate(int listenFd)
    {
        var fd = fcntl(listenFd, DuplicateCloexec, 0);
        if (fd < 0)
        {
            throw new IOException($"The display socket could not be duplicated, errno {Marshal.GetLastPInvokeError()}.");
        }

        var flags = fcntl(fd, GetStatusFlags, 0);
        if (flags < 0 || fcntl(fd, SetStatusFlags, flags | NonBlocking) < 0)
        {
            close(fd);
            throw new IOException($"The display socket could not be made non-blocking, errno {Marshal.GetLastPInvokeError()}.");
        }

        return fd;
    }

    public static DisplayAdoption Start(WaylandEventLoop loop, WlServerDisplay display, int ownedFd, Action<WlClient> adopted)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(adopted);
        var adoption = new DisplayAdoption(display, ownedFd, adopted);
        adoption._source = loop.AddFd(ownedFd, FdReadiness.Readable, (_, _) => adoption.Accept());
        return adoption;
    }

    public static void Close(int fd) => close(fd);

    private void Accept()
    {
        while (_source is not null)
        {
            var fd = accept4(_fd, 0, 0, SockCloexec);
            if (fd < 0)
            {
                return;
            }

            WlClient client;
            try
            {
                client = _display.CreateClient(fd);
            }
            catch (WaylandException)
            {
                close(fd);
                continue;
            }

            _adopted(client);
        }
    }

    public void Dispose()
    {
        if (_source is not { } source)
        {
            return;
        }

        _source = null;
        source.Remove();
        close(_fd);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int accept4(int fd, nint addr, nint addrlen, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    [DllImport("libc")]
    private static extern int close(int fd);
}
