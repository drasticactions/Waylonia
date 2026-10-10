using System.ComponentModel;

namespace Waylonia.Sessions;

internal sealed class WslLinkFactory : IWslLinkFactory
{
    public ISshLink Create(string destination, ISshPrompter prompter)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!WslDestination.TryParse(destination, out var distro))
        {
            throw new ArgumentException($"'{destination}' is not a WSL destination", nameof(destination));
        }

        return new WslLink(destination, distro);
    }

    public async Task<IReadOnlyList<string>> DistrosAsync(CancellationToken cancellation)
    {
        try
        {
            var (code, output, _) = await WslProcess.RunAsync(WslDestination.ListDistros(), cancellation).ConfigureAwait(false);
            return code == 0 ? WslDestination.ParseDistros(output) : [];
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }
}
