namespace HarborOps.Modules.Compliance.Contracts;

public interface IGetAuditPlanDetailQuery
{
    Task<GetAuditPlanDetailResponse?> GetAsync(
        Guid auditPlanId,
        CancellationToken cancellationToken);
}
