namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewQualificationEligibilityQuery
{
    Task<GetCrewQualificationEligibilityResponse> GetAsync(
        GetCrewQualificationEligibilityRequest request,
        CancellationToken cancellationToken);
}
