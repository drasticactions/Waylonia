namespace Waylonia.Accessibility;

/// <summary>
/// What the compositor knows about a Wayland toplevel, used to pair it with an accessible window.
/// </summary>
/// <param name="Pid">The process id of the Wayland client.</param>
/// <param name="Title">The xdg-toplevel title, or null when the client set none.</param>
/// <param name="ClientWidth">The width of the xdg window geometry, the client area.</param>
/// <param name="ClientHeight">The height of the xdg window geometry.</param>
/// <param name="SurfaceWidth">The width of the root wl_surface, shadow included.</param>
/// <param name="SurfaceHeight">The height of the root wl_surface.</param>
/// <param name="GeometryX">The x offset of the window geometry inside the surface, the CSD shadow margin.</param>
/// <param name="GeometryY">The y offset of the window geometry inside the surface.</param>
public sealed record A11yWindowTarget(
    int Pid,
    string? Title,
    int ClientWidth,
    int ClientHeight,
    int SurfaceWidth,
    int SurfaceHeight,
    int GeometryX,
    int GeometryY);
