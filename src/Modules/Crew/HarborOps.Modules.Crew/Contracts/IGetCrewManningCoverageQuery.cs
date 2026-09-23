namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewManningCoverageQuery
{
    Task<GetCrewManningCoverageResponse> GetAsync(
        GetCrewManningCoverageRequest request,
        CancellationToken cancellationToken);
}
