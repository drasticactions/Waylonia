using Basin.Hosted;
using Basin.Shell.Nested;
using Waylonia.Shell;

namespace Waylonia.Agent;

internal static class AgentShell
{
    public static PanelLayout Panels { get; } = new(0, 0, 0);

    public static PanelArrangement Arrangement { get; } = new(0, [], []);

    public static ShellSettings Settings(AgentProfile profile, ShellSettings configured)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(configured);
        return configured with
        {
            Placement = profile.Placement,
            Workspaces = 1,
            WorkspaceRows = 1,
            WorkspaceNames = [],
            Keys = [],
            FocusMode = FocusMode.Click,
        };
    }

    public static bool TakesOver(in BasinViewInput input) => input.Kind switch
    {
        BasinViewInputKind.PointerButton or BasinViewInputKind.Key => input.Pressed,
        BasinViewInputKind.TouchDown or BasinViewInputKind.PointerAxis => true,
        _ => false,
    };

    public static string Title(AgentProfile profile, string status)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return $"Waylonia agent {profile.Name} — {status}";
    }
}
