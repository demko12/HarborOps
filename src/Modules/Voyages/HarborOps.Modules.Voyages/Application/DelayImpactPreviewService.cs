using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class DelayImpactPreviewService(DbContext dbContext)
{
    public async Task<DelayImpactPreviewResponse> PreviewAsync(
        Guid voyageId,
        Guid delayId,
        CancellationToken cancellationToken)
    {
        var voyage = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var delay = await dbContext.Set<Delay>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == delayId && candidate.VoyageId == voyageId,
                cancellationToken)
            ?? throw new DelayNotFoundException(delayId);

        voyage.EnsureEstimatesRevisable();

        var alreadyApplied = await dbContext.Set<VoyageEstimateRevisionBatch>()
            .AsNoTracking()
            .AnyAsync(batch => batch.DelayId == delayId, cancellationToken);

        if (alreadyApplied)
        {
            throw new DelayImpactAlreadyAppliedException(delayId);
        }

        var stops = await (
            from stop in dbContext.Set<VoyageStop>().AsNoTracking()
            join port in dbContext.Set<Port>().AsNoTracking()
                on stop.PortId equals port.Id
            where stop.VoyageId == voyageId
            orderby stop.StopOrder, stop.Id
            select new DelayImpactStop(stop, port.Name))
            .ToListAsync(cancellationToken);

        var firstAffectedOrder = ResolveFirstAffectedOrder(delay, stops);

        var affected = stops
            .Where(candidate => IsAffected(candidate.Stop, firstAffectedOrder))
            .ToList();

        var proposals = affected
            .Select(candidate => ToProposal(candidate, delay.Duration))
            .ToList();

        var warnings = await ReadBerthConflictsAsync(affected, proposals, cancellationToken);

        var lastStop = stops.Count > 0 ? stops[^1].Stop : null;
        var arrivalDeltaMinutes = lastStop is null
            ? 0
            : proposals.Find(proposal => proposal.StopId == lastStop.Id)?.EtaDeltaMinutes ?? 0;

        return new DelayImpactPreviewResponse(
            voyageId,
            delayId,
            delay.Duration,
            voyage.PlannedArrival,
            voyage.PlannedArrival.AddMinutes(arrivalDeltaMinutes),
            arrivalDeltaMinutes,
            proposals,
            warnings);
    }

    private static DelayImpactStopItem ToProposal(DelayImpactStop candidate, int delayMinutes)
    {
        var stop = candidate.Stop;
        var etaDeltaMinutes = stop.Ata is null ? delayMinutes : 0;

        return new DelayImpactStopItem(
            stop.Id,
            stop.StopOrder,
            stop.PortId,
            candidate.PortName,
            stop.Status,
            stop.CurrentEta,
            stop.CurrentEtd,
            stop.CurrentEta.AddMinutes(etaDeltaMinutes),
            stop.CurrentEtd.AddMinutes(delayMinutes),
            etaDeltaMinutes,
            delayMinutes);
    }

    private static bool IsAffected(VoyageStop stop, int? firstAffectedOrder)
    {
        return !string.Equals(stop.Status, VoyageStop.DepartedStatus, StringComparison.Ordinal)
            && stop.Atd is null
            && (firstAffectedOrder is null || stop.StopOrder >= firstAffectedOrder);
    }

    private static int? ResolveFirstAffectedOrder(Delay delay, List<DelayImpactStop> stops)
    {
        if (delay.VoyageStopId is not { } voyageStopId)
        {
            return null;
        }

        var delayedStop = stops.Find(candidate => candidate.Stop.Id == voyageStopId)
            ?? throw new VoyageStopNotFoundException(voyageStopId);

        return delayedStop.Stop.StopOrder;
    }

    private async Task<List<BerthConflictWarningItem>> ReadBerthConflictsAsync(
        List<DelayImpactStop> affected,
        List<DelayImpactStopItem> proposals,
        CancellationToken cancellationToken)
    {
        var berthIds = affected
            .Where(candidate => candidate.Stop.BerthId is not null)
            .Select(candidate => candidate.Stop.BerthId!.Value)
            .Distinct()
            .ToList();

        if (berthIds.Count == 0)
        {
            return [];
        }

        var reservations = await (
            from schedule in dbContext.Set<BerthSchedule>().AsNoTracking()
            join berth in dbContext.Set<Berth>().AsNoTracking()
                on schedule.BerthId equals berth.Id
            where berthIds.Contains(schedule.BerthId)
                && BerthSchedule.ActiveStatuses.Contains(schedule.Status)
            select new BerthReservation(
                schedule.BerthId,
                schedule.VoyageStopId,
                berth.Name,
                schedule.StartTime,
                schedule.EndTime))
            .ToListAsync(cancellationToken);

        var warnings = new List<BerthConflictWarningItem>();

        foreach (var proposal in proposals)
        {
            var berthId = affected
                .Find(candidate => candidate.Stop.Id == proposal.StopId)?
                .Stop.BerthId;

            if (berthId is null)
            {
                continue;
            }

            warnings.AddRange(reservations
                .Where(reservation => reservation.BerthId == berthId
                    && reservation.VoyageStopId != proposal.StopId
                    && reservation.StartTime < proposal.ProposedEtd
                    && proposal.ProposedEta < reservation.EndTime)
                .Select(reservation => new BerthConflictWarningItem(
                    proposal.StopId,
                    proposal.StopOrder,
                    reservation.BerthId,
                    reservation.BerthName,
                    reservation.StartTime,
                    reservation.EndTime)));
        }

        return warnings;
    }

    private sealed record DelayImpactStop(VoyageStop Stop, string PortName);

    private sealed record BerthReservation(
        Guid BerthId,
        Guid VoyageStopId,
        string BerthName,
        DateTime StartTime,
        DateTime EndTime);
}
