namespace HarborOps.Modules.Voyages.Contracts;

public interface IRouteLookupQuery
{
    Task<RouteLookupResponse> LookupAsync(
        RouteLookupRequest request,
        CancellationToken cancellationToken);
}
