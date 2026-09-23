using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetVesselManningPlanDetailQuery(DbContext dbContext) : IGetVesselManningPlanDetailQuery
{
    public async Task<GetVesselManningPlanDetailResponse> GetAsync(
        Guid vesselManningPlanId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.Set<VesselManningPlan>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == vesselManningPlanId, cancellationToken);

        if (plan is null)
        {
            throw new VesselManningPlanNotFoundException(vesselManningPlanId);
        }

        var lines = await (
            from line in dbContext.Set<VesselManningPlanLine>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on line.RankId equals position.Id
            where line.VesselManningPlanId == vesselManningPlanId
            orderby position.RankName
            select new VesselManningPlanLineItem(line.Id, line.RankId, position.RankName, line.RequiredHeadcount))
            .ToListAsync(cancellationToken);

        var history = await dbContext.Set<VesselManningPlanHistory>()
            .AsNoTracking()
            .Where(entry => entry.VesselManningPlanId == vesselManningPlanId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new VesselManningPlanHistoryItem(
                entry.Id,
                entry.EventType,
                entry.Reason,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.PreviousEffectiveEndDate,
                entry.NewEffectiveEndDate))
            .ToListAsync(cancellationToken);

        return new GetVesselManningPlanDetailResponse(
            plan.Id,
            plan.VesselId,
            plan.EffectiveStartDate,
            plan.EffectiveEndDate,
            plan.CreatedByUserId,
            plan.CreatedAtUtc,
            plan.ConcurrencyToken,
            lines,
            history);
    }
}
