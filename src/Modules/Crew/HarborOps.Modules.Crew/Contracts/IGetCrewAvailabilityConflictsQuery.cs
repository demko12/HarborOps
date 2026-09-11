namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewAvailabilityConflictsQuery
{
    Task<GetCrewAvailabilityConflictsResponse> GetAsync(
        GetCrewAvailabilityConflictsRequest request,
        CancellationToken cancellationToken);
}
