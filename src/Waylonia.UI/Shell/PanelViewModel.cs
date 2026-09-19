using Waylonia.Shell.Applets;

namespace Waylonia.Shell;

internal sealed class PanelViewModel
{
    public PanelViewModel(PanelModel model, IReadOnlyList<PanelApplet> applets)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(applets);
        Model = model;
        Applets = applets.Select(applet => Create(model, applet)).ToArray();
    }

    public PanelModel Model { get; }

    public IReadOnlyList<object> Applets { get; }

    private static object Create(PanelModel model, PanelApplet applet) => applet.Kind switch
    {
        PanelAppletKind.MenuBar => new MenuBarViewModel(model),
        PanelAppletKind.WindowList => new WindowListViewModel(model),
        PanelAppletKind.WorkspaceSwitcher => new WorkspaceSwitcherViewModel(model),
        PanelAppletKind.Clock => new ClockViewModel(),
        _ => new SpacerViewModel(applet),
    };
}
