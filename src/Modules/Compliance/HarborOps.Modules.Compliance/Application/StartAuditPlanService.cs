using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class StartAuditPlanService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StartAuditPlanResponse> StartAsync(
        Guid auditPlanId,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.Set<AuditPlan>()
            .SingleOrDefaultAsync(candidate => candidate.Id == auditPlanId, cancellationToken)
            ?? throw new AuditPlanNotFoundException(auditPlanId);

        var previousStatus = plan.Status;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        plan.Start(occurredAtUtc);

        var history = AuditPlanHistory.RecordStarted(plan.Id, actingUserId, occurredAtUtc, previousStatus, plan.Status);

        await dbContext.Set<AuditPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartAuditPlanResponse(plan.Id, plan.Status, plan.StartedAtUtc!.Value);
    }
}
