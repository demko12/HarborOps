using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ReplaceVesselManningPlanLinesService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ReplaceVesselManningPlanLinesResponse> ReplaceAsync(
        Guid vesselManningPlanId,
        Guid actorUserId,
        ReplaceVesselManningPlanLinesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ReplaceVesselManningPlanLinesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ReplaceVesselManningPlanLinesValidationException(errors);
        }

        var requestedLines = request.Lines!;
        var rankIds = requestedLines.Select(line => line.RankId!.Value).Distinct().ToList();

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

        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        var plan = await dbContext.Set<VesselManningPlan>()
            .FirstOrDefaultAsync(p => p.Id == vesselManningPlanId, cancellationToken);

        if (plan is null)
        {
            throw new VesselManningPlanNotFoundException(vesselManningPlanId);
        }

        var existingLines = await dbContext.Set<VesselManningPlanLine>()
            .Where(line => line.VesselManningPlanId == vesselManningPlanId)
            .ToListAsync(cancellationToken);

        var previousSnapshot = VesselManningPlanLineSnapshotSerializer.Serialize(existingLines);

        var requestedByRankId = requestedLines.ToDictionary(line => line.RankId!.Value, line => line.RequiredHeadcount!.Value);
        var existingByRankId = existingLines.ToDictionary(line => line.RankId, line => line);

        foreach (var existingLine in existingLines)
        {
            if (!requestedByRankId.ContainsKey(existingLine.RankId))
            {
                dbContext.Set<VesselManningPlanLine>().Remove(existingLine);
            }
        }

        foreach (var requestedLine in requestedLines)
        {
            var rankId = requestedLine.RankId!.Value;
            var requiredHeadcount = requestedLine.RequiredHeadcount!.Value;

            if (existingByRankId.TryGetValue(rankId, out var existingLine))
            {
                existingLine.ChangeRequiredHeadcount(requiredHeadcount);
            }
            else
            {
                var newLine = VesselManningPlanLine.Create(vesselManningPlanId, rankId, requiredHeadcount);
                await dbContext.Set<VesselManningPlanLine>().AddAsync(newLine, cancellationToken);
            }
        }

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var newLines = requestedLines
            .Select(line => new VesselManningPlanLineSnapshot(line.RankId!.Value, line.RequiredHeadcount!.Value))
            .OrderBy(line => line.RankId);
        var newSnapshot = System.Text.Json.JsonSerializer.Serialize(newLines);

        var history = VesselManningPlanHistory.RecordLinesReplaced(
            vesselManningPlanId, actorUserId, request.Reason!.Trim(), occurredAtUtc, previousSnapshot, newSnapshot);

        plan.Touch();

        await dbContext.Set<VesselManningPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var rankNames = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .Where(position => rankIds.Contains(position.Id))
            .ToDictionaryAsync(position => position.Id, position => position.RankName, cancellationToken);

        var finalLines = await dbContext.Set<VesselManningPlanLine>()
            .AsNoTracking()
            .Where(line => line.VesselManningPlanId == vesselManningPlanId)
            .ToListAsync(cancellationToken);

        var lineItems = finalLines
            .Select(line => new VesselManningPlanLineItem(line.Id, line.RankId, rankNames[line.RankId], line.RequiredHeadcount))
            .ToList();

        return new ReplaceVesselManningPlanLinesResponse(plan.Id, plan.ConcurrencyToken, lineItems);
    }
}
