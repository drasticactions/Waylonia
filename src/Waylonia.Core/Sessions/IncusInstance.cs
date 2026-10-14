namespace Waylonia.Sessions;

internal sealed record IncusInstance(
    string Name,
    bool IsVm,
    bool IsRunning,
    string Project,
    uint? VsockId,
    IReadOnlyList<string> Devices,
    bool HasGpu);
