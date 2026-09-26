namespace Waylonia.Accessibility;

/// <summary>
/// An accessible top-level node paired with a Wayland window.
/// </summary>
/// <param name="RootId">The id of the frame, window or dialog node.</param>
/// <param name="Application">The accessible name of the application that owns it.</param>
/// <param name="Pid">The process id behind the application's bus name.</param>
/// <param name="OffsetX">What to subtract from an AT-SPI window-coordinate x to get a client-area x.</param>
/// <param name="OffsetY">What to subtract from an AT-SPI window-coordinate y to get a client-area y.</param>
public sealed record A11yWindow(string RootId, string Application, int Pid, int OffsetX, int OffsetY);
