using System.ComponentModel;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class IncusLinkFactory : ITargetLinkFactory
{
    public bool IsAvailable => CliProcess.OnPath(Incus.Program);

    public ISshLink Create(string destination, ISshPrompter prompter) => Create(destination, null, prompter);

    public ISshLink Create(string destination, string? user, ISshPrompter prompter)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!DestinationKinds.Incus.TryParse(destination, out var name))
        {
            throw new ArgumentException($"'{destination}' is not an Incus destination", nameof(destination));
        }

        return new IncusLink(destination, name, user);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellation)
    {
        try
        {
            var (code, output, _) = await CliProcess.RunAsync(Incus.Program, Incus.List(), cancellation).ConfigureAwait(false);
            return code == 0 ? [.. Incus.ParseInstances(output).Select(static instance => instance.Name)] : [];
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
