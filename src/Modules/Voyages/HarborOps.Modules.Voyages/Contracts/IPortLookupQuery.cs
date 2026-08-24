namespace HarborOps.Modules.Voyages.Contracts;

public interface IPortLookupQuery
{
    Task<PortLookupResponse> LookupAsync(
        PortLookupRequest request,
        CancellationToken cancellationToken);
}
