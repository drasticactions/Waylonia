namespace Waylonia.Accessibility;

/// <summary>
/// The rule that turns AT-SPI window coordinates into client-area coordinates for one window.
/// </summary>
public static class A11yOffsets
{
    /// <summary>
    /// How far, in logical pixels, a root node's size may differ from the surface or client size and still match it.
    /// </summary>
    public const int Tolerance = 4;

    /// <summary>
    /// The offset to subtract from a node's window-coordinate extents. A root node the size of the surface means the
    /// toolkit counts from the surface origin, shadow included, so the geometry offset is added; a root the size of
    /// the client area, or any other size, means the toolkit counts from the root node itself.
    /// </summary>
    /// <param name="rootExtents">The root node's own extents in window coordinates.</param>
    /// <param name="target">The Wayland window the root was paired with.</param>
    /// <returns>The offset.</returns>
    public static (int X, int Y) For(A11yBox rootExtents, A11yWindowTarget target)
    {
        var hasMargin = target.GeometryX != 0 || target.GeometryY != 0 ||
            target.SurfaceWidth != target.ClientWidth || target.SurfaceHeight != target.ClientHeight;
        if (hasMargin && Near(rootExtents, target.SurfaceWidth, target.SurfaceHeight))
        {
            return (rootExtents.X + target.GeometryX, rootExtents.Y + target.GeometryY);
        }

        return (rootExtents.X, rootExtents.Y);
    }

    /// <summary>
    /// Whether a box is within <see cref="Tolerance"/> of a size.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="width">The width to compare with.</param>
    /// <param name="height">The height to compare with.</param>
    /// <returns>True when both sides are close.</returns>
    public static bool Near(A11yBox box, int width, int height) =>
        Math.Abs(box.Width - width) <= Tolerance && Math.Abs(box.Height - height) <= Tolerance;

    /// <summary>
    /// How far a box's size is from a size, the sum of both sides' differences.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="width">The width to compare with.</param>
    /// <param name="height">The height to compare with.</param>
    /// <returns>The distance.</returns>
    public static int Distance(A11yBox box, int width, int height) =>
        Math.Abs(box.Width - width) + Math.Abs(box.Height - height);

    /// <summary>
    /// The largest coordinate a real window-relative box has. GTK3 reports uninitialized extents for a node that was
    /// never allocated, such as a hidden sidebar row, and those land far outside any window.
    /// </summary>
    public const int Limit = 1 << 16;

    /// <summary>
    /// Whether AT-SPI extents describe a box on a window: no negative size and no coordinate beyond <see cref="Limit"/>.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>True when the box can be shown.</returns>
    public static bool IsPlausible(int x, int y, int width, int height) =>
        width >= 0 && height >= 0 && width <= Limit && height <= Limit && Math.Abs((long)x) <= Limit && Math.Abs((long)y) <= Limit;
}
