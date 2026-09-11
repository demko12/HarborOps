using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetPortCallClearanceQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetPortCallClearanceQuery
{
    public async Task<PortCallClearanceResponse> GetAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var voyageExists = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .AnyAsync(voyage => voyage.Id == voyageId, cancellationToken);

        if (!voyageExists)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        var stopExists = await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .AnyAsync(stop => stop.Id == stopId && stop.VoyageId == voyageId, cancellationToken);

        if (!stopExists)
        {
            throw new VoyageStopNotFoundException(stopId);
        }

        var clearance = await dbContext.Set<PortCallClearance>()
            .AsNoTracking()
            .Where(candidate => candidate.VoyageStopId == stopId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Status,
                candidate.CompletedByUserId,
                candidate.CompletedAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PortCallClearanceNotFoundException(stopId);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var items = await dbContext.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .Where(item => item.PortCallClearanceId == clearance.Id)
            .OrderByDescending(item => item.IsRequired)
            .ThenBy(item => item.DueAtUtc)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Id)
            .Select(item => new PortCallClearanceItemModel(
                item.Id,
                item.Category,
                item.Title,
                item.IsRequired,
                item.DueAtUtc,
                item.AuthorityReference,
                item.EvidenceDocumentId,
                item.Status,
                item.IsRequired
                    && item.Status == PortCallClearanceItem.PendingStatus
                    && item.DueAtUtc != null
                    && item.DueAtUtc < utcNow,
                item.DecisionReason,
                item.DecidedByUserId,
                item.DecidedAtUtc,
                item.AddedByUserId,
                item.AddedAtUtc))
            .ToListAsync(cancellationToken);

        var history = await dbContext.Set<PortCallClearanceEvent>()
            .AsNoTracking()
            .Where(recorded => recorded.PortCallClearanceId == clearance.Id)
            .OrderBy(recorded => recorded.Sequence)
            .Select(recorded => new PortCallClearanceEventModel(
                recorded.Id,
                recorded.PortCallClearanceItemId,
                recorded.Sequence,
                recorded.EventType,
                recorded.Reason,
                recorded.ActorUserId,
                recorded.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return new PortCallClearanceResponse(
            voyageId,
            stopId,
            clearance.Id,
            clearance.Status,
            clearance.CompletedByUserId,
            clearance.CompletedAtUtc,
            utcNow,
            BuildProgress(items),
            items,
            history);
    }

    private static PortCallClearanceProgress BuildProgress(
        IReadOnlyList<PortCallClearanceItemModel> items)
    {
        var requiredItemCount = items.Count(item => item.IsRequired);
        var requiredPendingCount = items.Count(item =>
            item.IsRequired && IsPending(item));
        var optionalItemCount = items.Count - requiredItemCount;
        var optionalPendingCount = items.Count(item => !item.IsRequired && IsPending(item));

        return new PortCallClearanceProgress(
            items.Count,
            requiredItemCount,
            requiredItemCount - requiredPendingCount,
            requiredPendingCount,
            items.Count(item => item.IsOverdue),
            optionalItemCount,
            optionalPendingCount,
            items.Count(item => string.Equals(
                item.Status,
                PortCallClearanceItem.ClearedStatus,
                StringComparison.Ordinal)),
            items.Count(item => string.Equals(
                item.Status,
                PortCallClearanceItem.WaivedStatus,
                StringComparison.Ordinal)),
            requiredItemCount > 0 && requiredPendingCount == 0);
    }

    private static bool IsPending(PortCallClearanceItemModel item) =>
        string.Equals(item.Status, PortCallClearanceItem.PendingStatus, StringComparison.Ordinal);
}
