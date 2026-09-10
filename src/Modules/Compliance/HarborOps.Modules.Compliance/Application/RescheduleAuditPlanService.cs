using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RescheduleAuditPlanService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RescheduleAuditPlanResponse> RescheduleAsync(
        Guid auditPlanId,
        Guid actingUserId,
        RescheduleAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RescheduleAuditPlanValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RescheduleAuditPlanValidationException(errors);
        }

        var plan = await dbContext.Set<AuditPlan>()
            .SingleOrDefaultAsync(candidate => candidate.Id == auditPlanId, cancellationToken)
            ?? throw new AuditPlanNotFoundException(auditPlanId);

        if (plan.Status != AuditPlan.ScheduledStatus)
        {
            throw new InvalidAuditPlanTransitionException(plan.Id, plan.Status, AuditPlan.RescheduleAction);
        }

        var plannedStartDate = request.PlannedStartDate!.Value;
        var dueDate = request.DueDate!.Value;
        var reason = request.Reason!.Trim();

        await AuditPlanOverlap.EnsureNoOverlapAsync(
            dbContext, plan.VesselId, plan.Type, plannedStartDate, dueDate, plan.Id, cancellationToken);

        var previousPlannedStartDate = plan.PlannedStartDate;
        var previousDueDate = plan.DueDate;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        plan.Reschedule(plannedStartDate, dueDate);

        var history = AuditPlanHistory.RecordRescheduled(
            plan.Id,
            reason,
            actingUserId,
            occurredAtUtc,
            plan.Status,
            previousPlannedStartDate,
            plannedStartDate,
            previousDueDate,
            dueDate);

        await dbContext.Set<AuditPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RescheduleAuditPlanResponse(plan.Id, plan.PlannedStartDate, plan.DueDate);
    }
}
