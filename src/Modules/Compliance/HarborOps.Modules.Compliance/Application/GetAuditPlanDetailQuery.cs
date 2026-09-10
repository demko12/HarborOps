using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetAuditPlanDetailQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetAuditPlanDetailQuery
{
    public async Task<GetAuditPlanDetailResponse?> GetAsync(
        Guid auditPlanId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.Set<AuditPlan>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == auditPlanId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.VesselId,
                candidate.Type,
                candidate.Status,
                candidate.PlannedStartDate,
                candidate.DueDate,
                candidate.AssignedInspectorUserId,
                candidate.Scope,
                candidate.Notes,
                candidate.SurveyAuditId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var history = await dbContext.Set<AuditPlanHistory>()
            .AsNoTracking()
            .Where(entry => entry.AuditPlanId == auditPlanId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new AuditPlanHistoryItem(
                entry.Id,
                entry.EventType,
                entry.Reason,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.PreviousStatus,
                entry.NewStatus,
                entry.PreviousPlannedStartDate,
                entry.NewPlannedStartDate,
                entry.PreviousDueDate,
                entry.NewDueDate,
                entry.PreviousInspectorUserId,
                entry.NewInspectorUserId))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var isOverdue = (plan.Status == AuditPlan.ScheduledStatus || plan.Status == AuditPlan.InProgressStatus)
            && plan.DueDate < today;

        return new GetAuditPlanDetailResponse(
            plan.Id,
            plan.VesselId,
            plan.Type,
            plan.Status,
            plan.PlannedStartDate,
            plan.DueDate,
            plan.AssignedInspectorUserId,
            plan.Scope,
            plan.Notes,
            isOverdue,
            plan.SurveyAuditId,
            history);
    }
}
