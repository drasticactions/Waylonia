using Basin.XWayland;

namespace Waylonia;

internal sealed class LinuxHostCursor : IHostCursor
{
    private X11Pointer? _pointer;
    private bool _tried;

    public (int X, int Y)? TryGetPosition()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return null;
        }

        if (!_tried)
        {
            _tried = true;
            _pointer = X11Pointer.TryConnect();
        }

        return _pointer?.TryGetPosition() is { } position
            ? (position.X, position.Y)
            : null;
    }

    public void Close()
    {
        _pointer?.Dispose();
        _pointer = null;
        _tried = false;
    }
}
