namespace HarborOps.Modules.Voyages.Contracts;

public interface IBerthLookupQuery
{
    Task<BerthLookupResponse> LookupAsync(
        BerthLookupRequest request,
        CancellationToken cancellationToken);
}
