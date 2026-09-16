namespace Waylonia.Shell.Applets;

internal sealed class SpacerViewModel(PanelApplet applet) : IExpandingApplet
{
    public PanelApplet Applet { get; } = applet ?? throw new ArgumentNullException(nameof(applet));
}
