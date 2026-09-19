using Avalonia;

namespace Waylonia;

internal static class HostCursorPixels
{
    public static PixelPoint? TryGetPixelPoint(this IHostCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return cursor.TryGetPosition() is { } position ? new PixelPoint(position.X, position.Y) : null;
    }
}
