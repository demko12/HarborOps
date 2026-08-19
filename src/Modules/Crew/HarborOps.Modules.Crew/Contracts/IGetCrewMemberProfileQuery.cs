namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewMemberProfileQuery
{
    Task<CrewMemberProfileResponse> GetAsync(Guid crewMemberId, CancellationToken cancellationToken);
}
