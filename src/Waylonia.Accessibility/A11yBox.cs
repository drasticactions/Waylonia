namespace Waylonia.Accessibility;

/// <summary>
/// A rectangle in logical pixels, relative to the client area of the window it belongs to.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct A11yBox(int X, int Y, int Width, int Height);
