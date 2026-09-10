using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class CancelAuditPlanService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CancelAuditPlanResponse> CancelAsync(
        Guid auditPlanId,
        Guid actingUserId,
        CancelAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CancelAuditPlanValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CancelAuditPlanValidationException(errors);
        }

        var plan = await dbContext.Set<AuditPlan>()
            .SingleOrDefaultAsync(candidate => candidate.Id == auditPlanId, cancellationToken)
            ?? throw new AuditPlanNotFoundException(auditPlanId);

        if (plan.Status is not (AuditPlan.ScheduledStatus or AuditPlan.InProgressStatus))
        {
            throw new InvalidAuditPlanTransitionException(plan.Id, plan.Status, AuditPlan.CancelAction);
        }

        var previousStatus = plan.Status;
        var reason = request.Reason!.Trim();
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        plan.Cancel(reason, occurredAtUtc);

        var history = AuditPlanHistory.RecordCancelled(plan.Id, reason, actingUserId, occurredAtUtc, previousStatus, plan.Status);

        await dbContext.Set<AuditPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CancelAuditPlanResponse(plan.Id, plan.Status, plan.CancelledAtUtc!.Value);
    }
}
