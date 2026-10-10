namespace Waylonia.Sessions;

internal interface IWslLinkFactory : ISshLinkFactory
{
    Task<IReadOnlyList<string>> DistrosAsync(CancellationToken cancellation);
}
