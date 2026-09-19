using System.Runtime.InteropServices;

namespace Waylonia;

internal sealed class WindowsHostCursor : IHostCursor
{
    public (int X, int Y)? TryGetPosition() =>
        GetCursorPos(out var point) ? (point.X, point.Y) : null;

    public void Close()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;

        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
