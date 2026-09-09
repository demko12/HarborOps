namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetAuditPlansQuery
{
    Task<GetAuditPlansResponse> GetAsync(
        GetAuditPlansRequest request,
        CancellationToken cancellationToken);
}
