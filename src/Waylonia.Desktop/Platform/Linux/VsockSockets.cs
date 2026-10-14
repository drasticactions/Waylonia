using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal static unsafe class VsockSockets
{
    public const uint AnyCid = 0xFFFFFFFF;

    private const int VsockFamily = 40;
    private const int SockStream = 1;
    private const int SockCloexec = 0x80000;
    private const int AddressLength = 16;
    private const int AddressInUse = 98;
    private const short PollIn = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    public static int Listen(uint port, out bool inUse)
    {
        inUse = false;
        var fd = socket(VsockFamily, SockStream | SockCloexec, 0);
        if (fd < 0)
        {
            throw new SocketException(Marshal.GetLastPInvokeError());
        }

        var address = stackalloc byte[AddressLength];
        Write(new Span<byte>(address, AddressLength), AnyCid, port);
        if (bind(fd, address, AddressLength) != 0 || listen(fd, 8) != 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            _ = close(fd);
            inUse = errno == AddressInUse;
            throw new SocketException(errno);
        }

        return fd;
    }

    public static int Accept(int fd, int timeoutMs)
    {
        var poll = new PollFd { Fd = fd, Events = PollIn };
        return VsockSockets.poll(&poll, 1, timeoutMs) > 0 && (poll.REvents & PollIn) != 0 ? accept4(fd, 0, 0, SockCloexec) : -1;
    }

    public static void Close(int fd) => _ = close(fd);

    public static uint? PeerCid(int fd)
    {
        var address = stackalloc byte[AddressLength];
        var length = AddressLength;
        if (getpeername(fd, address, &length) != 0 || length < 12)
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(address + 8, 4));
    }

    private static void Write(Span<byte> address, uint cid, uint port)
    {
        address.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(address, VsockFamily);
        BinaryPrimitives.WriteUInt32LittleEndian(address[4..], port);
        BinaryPrimitives.WriteUInt32LittleEndian(address[8..], cid);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern int bind(int fd, byte* address, int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpeername(int fd, byte* address, int* length);

    [DllImport("libc", SetLastError = true)]
    private static extern int accept4(int fd, nint address, nint length, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(PollFd* fds, nuint count, int timeoutMs);

    [DllImport("libc")]
    private static extern int close(int fd);
}
