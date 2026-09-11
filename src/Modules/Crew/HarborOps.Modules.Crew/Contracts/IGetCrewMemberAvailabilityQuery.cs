namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewMemberAvailabilityQuery
{
    Task<GetCrewMemberAvailabilityResponse> GetAsync(Guid crewMemberId, CancellationToken cancellationToken);
}
