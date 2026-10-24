using System.ComponentModel;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class PodmanBoxLinkFactory(PodmanBox box) : ITargetLinkFactory
{
    public bool IsAvailable => CliProcess.OnPath(box.Program);

    public ISshLink Create(string destination, ISshPrompter prompter)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!box.Kind.TryParse(destination, out var name))
        {
            throw new ArgumentException($"The destination '{destination}' is not a {box.Program} container. Give {box.Program}:NAME.", nameof(destination));
        }

        return new PodmanBoxLink(box, destination, name);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellation)
    {
        try
        {
            var (code, output, _) = await CliProcess.RunAsync(
                box.Program, box.ListArguments, cancellation, box.Environment(CliProcess.HostEnvironment())).ConfigureAwait(false);
            return code == 0 ? box.ParseList(output) : [];
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Log.Debug(error, $"{box.Program}: containers not listed");
            return [];
        }
    }
}
