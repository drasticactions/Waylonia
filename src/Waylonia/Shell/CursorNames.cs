using Avalonia.Input;
using Basin.Avalonia;
using Basin.Capabilities;

namespace Waylonia.Shell;

internal static class CursorNames
{
    public static Cursor For(string name) => name switch
    {
        "top_side" or "n-resize" => AvaloniaCursor.For(CursorShape.NResize),
        "bottom_side" or "s-resize" => AvaloniaCursor.For(CursorShape.SResize),
        "left_side" or "w-resize" => AvaloniaCursor.For(CursorShape.WResize),
        "right_side" or "e-resize" => AvaloniaCursor.For(CursorShape.EResize),
        "top_left_corner" or "nw-resize" => AvaloniaCursor.For(CursorShape.NwResize),
        "top_right_corner" or "ne-resize" => AvaloniaCursor.For(CursorShape.NeResize),
        "bottom_left_corner" or "sw-resize" => AvaloniaCursor.For(CursorShape.SwResize),
        "bottom_right_corner" or "se-resize" => AvaloniaCursor.For(CursorShape.SeResize),
        "sb_h_double_arrow" or "ew-resize" or "col-resize" => AvaloniaCursor.For(CursorShape.EwResize),
        "sb_v_double_arrow" or "ns-resize" or "row-resize" => AvaloniaCursor.For(CursorShape.NsResize),
        "fleur" or "move" or "all-scroll" => AvaloniaCursor.For(CursorShape.Move),
        "hand2" or "hand1" or "pointer" or "grab" => AvaloniaCursor.For(CursorShape.Pointer),
        "xterm" or "text" => AvaloniaCursor.For(CursorShape.Text),
        "watch" or "wait" => AvaloniaCursor.For(CursorShape.Wait),
        "left_ptr_watch" or "progress" => AvaloniaCursor.For(CursorShape.Progress),
        "crosshair" => AvaloniaCursor.For(CursorShape.Crosshair),
        "question_arrow" or "help" => AvaloniaCursor.For(CursorShape.Help),
        "crossed_circle" or "not-allowed" => AvaloniaCursor.For(CursorShape.NotAllowed),
        _ => AvaloniaCursor.For(CursorShape.Default),
    };
}
