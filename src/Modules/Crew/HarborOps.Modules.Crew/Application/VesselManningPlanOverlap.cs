using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

/// <summary>
/// Inclusive-boundary overlap check for vessel manning plans. Two periods
/// <c>[a.StartDate, a.EndDate]</c> and <c>[b.StartDate, b.EndDate]</c> overlap when
/// <c>a.StartDate &lt;= b.EndDate &amp;&amp; b.StartDate &lt;= a.EndDate</c> — touching endpoints count.
/// </summary>
internal static class VesselManningPlanOverlap
{
    internal static async Task EnsureNoOverlappingPlanAsync(
        DbContext dbContext,
        Guid vesselId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludePlanId,
        CancellationToken cancellationToken)
    {
        var overlaps = await dbContext.Set<VesselManningPlan>()
            .AsNoTracking()
            .AnyAsync(
                plan => plan.VesselId == vesselId
                    && plan.Id != excludePlanId
                    && plan.EffectiveStartDate <= endDate
                    && startDate <= plan.EffectiveEndDate,
                cancellationToken);

        if (overlaps)
        {
            throw new OverlappingVesselManningPlanException(vesselId, startDate, endDate);
        }
    }
}
