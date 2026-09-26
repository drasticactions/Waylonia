namespace Waylonia.Agent;

internal sealed record AgentAllowEntry(string Program, IReadOnlyList<string> Arguments)
{
    public bool IsDesktop => Program.EndsWith(".desktop", StringComparison.Ordinal);

    public string DesktopId => IsDesktop ? Program : Program + ".desktop";

    public override string ToString() => Arguments.Count == 0 ? Program : $"{Program} {string.Join(' ', Arguments)}";
}
