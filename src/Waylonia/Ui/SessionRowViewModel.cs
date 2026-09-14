using Waylonia.Sessions;

namespace Waylonia.Ui;

internal sealed class SessionRowViewModel(string name, string detail, SessionProfile? profile, BrokenSession? broken)
{
    public string Name { get; } = name;

    public string Detail { get; } = detail;

    public SessionProfile? Profile { get; } = profile;

    public BrokenSession? Broken { get; } = broken;

    public string Text => Detail.Length == 0 ? Name : $"{Name}\n{Detail}";
}
