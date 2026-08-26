namespace HarborOps.Modules.Crew.Contracts;

public interface IGetMemberMedicalExaminationsQuery
{
    Task<GetMemberMedicalExaminationsResponse> GetAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken);
}
