using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CreateVesselManningPlanService(
    DbContext dbContext,
    IVesselExistenceLookup vesselExistenceLookup,
    TimeProvider timeProvider)
{
    public async Task<CreateVesselManningPlanResponse> CreateAsync(
        Guid createdByUserId,
        CreateVesselManningPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreateVesselManningPlanValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreateVesselManningPlanValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var startDate = request.EffectiveStartDate!.Value;
        var endDate = request.EffectiveEndDate!.Value;
        var lines = request.Lines!;

        var vessel = await vesselExistenceLookup.GetAsync(vesselId, cancellationToken);
        if (vessel is null)
        {
            throw new VesselNotFoundException(vesselId);
        }

        var rankIds = lines.Select(line => line.RankId!.Value).Distinct().ToList();
        var existingRankIds = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .Where(position => rankIds.Contains(position.Id))
            .Select(position => position.Id)
            .ToListAsync(cancellationToken);

        var missingRankId = rankIds.FirstOrDefault(rankId => !existingRankIds.Contains(rankId));
        if (missingRankId != Guid.Empty)
        {
            throw new CrewPositionNotFoundException(missingRankId);
        }

        await VesselManningPlanOverlap.EnsureNoOverlappingPlanAsync(
            dbContext, vesselId, startDate, endDate, excludePlanId: null, cancellationToken);

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var plan = VesselManningPlan.Create(vesselId, startDate, endDate, createdByUserId, occurredAtUtc);
        var planLines = lines
            .Select(line => VesselManningPlanLine.Create(plan.Id, line.RankId!.Value, line.RequiredHeadcount!.Value))
            .ToList();

        var history = VesselManningPlanHistory.RecordCreated(
            plan.Id,
            createdByUserId,
            occurredAtUtc,
            VesselManningPlanLineSnapshotSerializer.Serialize(planLines));

        await dbContext.Set<VesselManningPlan>().AddAsync(plan, cancellationToken);
        await dbContext.Set<VesselManningPlanLine>().AddRangeAsync(planLines, cancellationToken);
        await dbContext.Set<VesselManningPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rankNames = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .Where(position => rankIds.Contains(position.Id))
            .ToDictionaryAsync(position => position.Id, position => position.RankName, cancellationToken);

        var lineItems = planLines
            .Select(line => new VesselManningPlanLineItem(line.Id, line.RankId, rankNames[line.RankId], line.RequiredHeadcount))
            .ToList();

        return new CreateVesselManningPlanResponse(
            plan.Id, plan.VesselId, plan.EffectiveStartDate, plan.EffectiveEndDate, plan.ConcurrencyToken, lineItems);
    }
}
