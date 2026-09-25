namespace HarborOps.Modules.Crew.Contracts;

public interface IGetQualificationPolicyDetailQuery
{
    Task<GetQualificationPolicyDetailResponse> GetAsync(
        Guid policyId,
        CancellationToken cancellationToken);
}
