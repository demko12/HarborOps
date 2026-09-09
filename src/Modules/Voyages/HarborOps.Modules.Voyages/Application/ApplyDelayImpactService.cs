using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class ApplyDelayImpactService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ApplyDelayImpactResponse> ApplyAsync(
        Guid voyageId,
        Guid delayId,
        Guid actingUserId,
        ApplyDelayImpactRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ApplyDelayImpactValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ApplyDelayImpactValidationException(errors);
        }

        var voyage = await dbContext.Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var delayExists = await dbContext.Set<Delay>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == delayId && candidate.VoyageId == voyageId,
                cancellationToken);

        if (!delayExists)
        {
            throw new DelayNotFoundException(delayId);
        }

        voyage.EnsureEstimatesRevisable();

        var alreadyApplied = await dbContext.Set<VoyageEstimateRevisionBatch>()
            .AsNoTracking()
            .AnyAsync(batch => batch.DelayId == delayId, cancellationToken);

        if (alreadyApplied)
        {
            throw new DelayImpactAlreadyAppliedException(delayId);
        }

        var stops = await dbContext.Set<VoyageStop>()
            .Where(stop => stop.VoyageId == voyageId)
            .OrderBy(stop => stop.StopOrder)
            .ThenBy(stop => stop.Id)
            .ToListAsync(cancellationToken);

        var batch = VoyageEstimateRevisionBatch.Record(
            voyageId,
            delayId,
            request.Reason!.Trim(),
            actingUserId,
            timeProvider.GetUtcNow().UtcDateTime);

        var items = new List<VoyageEstimateRevisionItem>();

        foreach (var requestedStop in request.Stops!)
        {
            var voyageStopId = requestedStop.VoyageStopId!.Value;
            var stop = stops.Find(candidate => candidate.Id == voyageStopId)
                ?? throw new VoyageStopNotFoundException(voyageStopId);

            var previousEta = stop.CurrentEta;
            var previousEtd = stop.CurrentEtd;

            if (previousEta != requestedStop.ExpectedEta!.Value
                || previousEtd != requestedStop.ExpectedEtd!.Value)
            {
                throw new StaleDelayImpactPreviewException(stop.Id);
            }

            stop.ReviseEstimates(requestedStop.Eta!.Value, requestedStop.Etd!.Value);

            items.Add(VoyageEstimateRevisionItem.Record(
                batch.Id,
                stop.Id,
                previousEta,
                previousEtd,
                stop.CurrentEta,
                stop.CurrentEtd));
        }

        EnsureChronology(voyage, stops);

        await dbContext.Set<VoyageEstimateRevisionBatch>().AddAsync(batch, cancellationToken);
        await dbContext.Set<VoyageEstimateRevisionItem>().AddRangeAsync(items, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await WasAppliedConcurrentlyAsync(batch.Id, delayId, cancellationToken))
            {
                throw new DelayImpactAlreadyAppliedException(delayId);
            }

            throw;
        }

        var portNames = await ReadPortNamesAsync(stops, cancellationToken);

        return new ApplyDelayImpactResponse(
            batch.Id,
            batch.VoyageId,
            batch.DelayId,
            batch.Reason,
            batch.RevisedByUserId,
            batch.RevisedAtUtc,
            items
                .Select(item => new
                {
                    Item = item,
                    Stop = stops.Find(stop => stop.Id == item.VoyageStopId)!,
                })
                .OrderBy(entry => entry.Stop.StopOrder)
                .ThenBy(entry => entry.Stop.Id)
                .Select(entry => new EstimateRevisionStopChangeItem(
                    entry.Stop.Id,
                    entry.Stop.StopOrder,
                    portNames[entry.Stop.PortId],
                    entry.Item.PreviousEta,
                    entry.Item.PreviousEtd,
                    entry.Item.NewEta,
                    entry.Item.NewEtd))
                .ToList());
    }

    private async Task<bool> WasAppliedConcurrentlyAsync(
        Guid batchId,
        Guid delayId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<VoyageEstimateRevisionBatch>()
            .AsNoTracking()
            .AnyAsync(batch => batch.DelayId == delayId && batch.Id != batchId, cancellationToken);
    }

    private static void EnsureChronology(Voyage voyage, List<VoyageStop> orderedStops)
    {
        var earliestAllowed = voyage.ActualDeparture;

        foreach (var stop in orderedStops)
        {
            if (stop.Atd is { } actualDeparture)
            {
                earliestAllowed = Later(earliestAllowed, actualDeparture);

                continue;
            }

            if (stop.Ata is { } actualArrival)
            {
                earliestAllowed = Later(
                    Later(earliestAllowed, actualArrival),
                    stop.CurrentEtd);

                continue;
            }

            if (stop.CurrentEtd < stop.CurrentEta)
            {
                throw new VoyageEstimateChronologyException(
                    stop.Id,
                    stop.CurrentEtd,
                    stop.CurrentEta);
            }

            if (earliestAllowed is { } boundary && stop.CurrentEta < boundary)
            {
                throw new VoyageEstimateChronologyException(stop.Id, stop.CurrentEta, boundary);
            }

            earliestAllowed = stop.CurrentEtd;
        }
    }

    private static DateTime Later(DateTime? left, DateTime right)
    {
        return left is { } value && value > right ? value : right;
    }

    private async Task<Dictionary<Guid, string>> ReadPortNamesAsync(
        List<VoyageStop> stops,
        CancellationToken cancellationToken)
    {
        var portIds = stops.Select(stop => stop.PortId).Distinct().ToList();

        return await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(port => portIds.Contains(port.Id))
            .ToDictionaryAsync(port => port.Id, port => port.Name, cancellationToken);
    }
}
