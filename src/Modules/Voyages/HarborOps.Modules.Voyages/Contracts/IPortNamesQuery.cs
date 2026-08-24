namespace HarborOps.Modules.Voyages.Contracts;

public interface IPortNamesQuery
{
    Task<PortNamesResponse> ResolveAsync(
        PortNamesRequest request,
        CancellationToken cancellationToken);
}
