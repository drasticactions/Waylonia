using Basin;

namespace Waylonia.Shell;

internal readonly record struct PlacementRequest(
    int Width,
    int Height,
    Box WorkArea,
    IReadOnlyList<Box> Visible,
    Point Pointer,
    PlacementMode Mode,
    bool CenterNewWindows,
    Box? ParentFrame,
    int ParentTitleHeight,
    Box Output);
