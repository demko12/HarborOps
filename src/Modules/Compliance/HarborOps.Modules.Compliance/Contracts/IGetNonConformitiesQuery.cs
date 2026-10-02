namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetNonConformitiesQuery
{
    Task<GetNonConformitiesResponse> GetAsync(
        GetNonConformitiesRequest request,
        CancellationToken cancellationToken);

    Task<GetNonConformitiesSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken);
}
