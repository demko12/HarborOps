namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewEmploymentStatusHistoryQuery
{
    Task<GetCrewEmploymentStatusHistoryResponse> GetAsync(Guid crewMemberId, CancellationToken cancellationToken);
}
