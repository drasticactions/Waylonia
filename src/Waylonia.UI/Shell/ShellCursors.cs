using Avalonia.Input;
using Basin.Avalonia;
using Basin.Shell.Nested;

namespace Waylonia.Shell;

internal static class ShellCursors
{
    public static bool TryToAvalonia(ShellCursor cursor, out Cursor? avalonia)
    {
        if (cursor.Shape is { } shape)
        {
            avalonia = AvaloniaCursor.For(shape);
            return true;
        }

        if (cursor.Surface is { } surface)
        {
            avalonia = AvaloniaCursor.FromSurface(surface, cursor.HotspotX, cursor.HotspotY);
            return avalonia is not null;
        }

        avalonia = cursor.Hidden ? new Cursor(StandardCursorType.None) : null;
        return true;
    }
}
