namespace HarborOps.Modules.Crew.Contracts;

public interface IGetQualificationPoliciesQuery
{
    Task<GetQualificationPoliciesResponse> GetAsync(
        GetQualificationPoliciesRequest request,
        CancellationToken cancellationToken);
}
