using System.Runtime.InteropServices;

namespace Waylonia;

internal sealed class MacHostCursor : IHostCursor
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const uint CombinedSessionState = 0;

    public (int X, int Y)? TryGetPosition()
    {
        var source = CGEventSourceCreate(CombinedSessionState);
        var carbonEvent = CGEventCreate(source);
        if (source != IntPtr.Zero)
        {
            CFRelease(source);
        }

        if (carbonEvent == IntPtr.Zero)
        {
            return null;
        }

        var location = CGEventGetLocation(carbonEvent);
        CFRelease(carbonEvent);
        return ((int)location.X, (int)location.Y);
    }

    public void Close()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;

        public double Y;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventSourceCreate(uint stateId);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr carbonEvent);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr reference);
}
