namespace HarborOps.Modules.Documents.Contracts;

public interface IGetRetentionPoliciesQuery
{
    Task<GetRetentionPoliciesResponse> GetRetentionPoliciesAsync(CancellationToken cancellationToken);
}
