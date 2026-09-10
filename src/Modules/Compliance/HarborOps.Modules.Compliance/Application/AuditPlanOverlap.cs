using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal static class AuditPlanOverlap
{
    // An active plan (Scheduled or InProgress) for the same vessel and audit type overlaps
    // when the two date ranges intersect, inclusive of touching endpoints - i.e. a plan due
    // on the day another one starts still counts as an overlap.
    internal static async Task EnsureNoOverlapAsync(
        DbContext dbContext,
        Guid vesselId,
        string type,
        DateOnly plannedStartDate,
        DateOnly dueDate,
        Guid? excludePlanId,
        CancellationToken cancellationToken)
    {
        var candidates = dbContext.Set<AuditPlan>()
            .AsNoTracking()
            .Where(plan => plan.VesselId == vesselId && plan.Type == type)
            .Where(plan => plan.Status == AuditPlan.ScheduledStatus || plan.Status == AuditPlan.InProgressStatus);

        if (excludePlanId is { } id)
        {
            candidates = candidates.Where(plan => plan.Id != id);
        }

        var overlaps = await candidates.AnyAsync(
            plan => plan.PlannedStartDate <= dueDate && plannedStartDate <= plan.DueDate,
            cancellationToken);

        if (overlaps)
        {
            throw new OverlappingAuditPlanException(vesselId, type);
        }
    }
}
