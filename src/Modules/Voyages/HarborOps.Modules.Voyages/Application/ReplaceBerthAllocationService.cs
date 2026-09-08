using System.Data;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HarborOps.Modules.Voyages.Application;

public sealed class ReplaceBerthAllocationService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ReplaceBerthAllocationResponse> ReplaceAsync(Guid voyageId, Guid stopId, ReplaceBerthAllocationRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = Validate(request);
        if (errors.Count > 0) throw new ReplaceBerthAllocationValidationException(errors);
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational()) transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var voyage = await dbContext.Set<Voyage>().SingleOrDefaultAsync(item => item.Id == voyageId, cancellationToken) ?? throw new VoyageNotFoundException(voyageId);
            if (!string.Equals(voyage.Status, Voyage.PlannedStatus, StringComparison.Ordinal)) throw new VoyageBerthAllocationNotAllowedException(voyageId, voyage.Status);
            var stop = await dbContext.Set<VoyageStop>().SingleOrDefaultAsync(item => item.Id == stopId && item.VoyageId == voyageId, cancellationToken) ?? throw new VoyageStopNotFoundException(stopId);
            var berth = await dbContext.Set<Berth>().SingleOrDefaultAsync(item => item.Id == request.BerthId, cancellationToken) ?? throw new BerthNotFoundException(request.BerthId!.Value);
            if (berth.PortId != stop.PortId) throw new BerthPortMismatchException(berth.Id, stop.PortId);
            var start = request.PlannedArrivalUtc!.Value;
            var end = request.PlannedDepartureUtc!.Value;
            var overlaps = await dbContext.Set<BerthSchedule>().AnyAsync(schedule => schedule.BerthId == berth.Id && schedule.VoyageStopId != stop.Id && BerthSchedule.ActiveStatuses.Contains(schedule.Status) && schedule.StartTime < end && start < schedule.EndTime, cancellationToken);
            if (overlaps) throw new BerthOverlapException(berth.Id, start, end);
            var existing = await dbContext.Set<BerthSchedule>().Where(schedule => schedule.VoyageStopId == stop.Id && BerthSchedule.ActiveStatuses.Contains(schedule.Status)).ToListAsync(cancellationToken);
            foreach (var schedule in existing) schedule.Release();
            var previousBerthId = stop.BerthId;
            var previousEta = stop.Eta;
            var previousEtd = stop.Etd;
            stop.ReplaceBerthAllocation(berth.Id, start, end);
            await dbContext.Set<BerthSchedule>().AddAsync(BerthSchedule.Create(berth.Id, stop.Id, start, end), cancellationToken);
            var changedAt = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.Set<BerthAllocationChange>().AddAsync(BerthAllocationChange.Record(voyageId, stopId, previousBerthId, berth.Id, previousEta, previousEtd, start, end, request.Reason!.Trim(), actorId, changedAt), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new ReplaceBerthAllocationResponse(stop.Id, berth.Id, start, end, changedAt);
        }
        catch (DbUpdateException exception)
        {
            throw new BerthOverlapException(request.BerthId!.Value, request.PlannedArrivalUtc!.Value, request.PlannedDepartureUtc!.Value, exception);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private static IReadOnlyDictionary<string, string[]> Validate(ReplaceBerthAllocationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.BerthId is null || request.BerthId == Guid.Empty) errors[nameof(request.BerthId)] = ["Berth is required."];
        if (request.PlannedArrivalUtc is null) errors[nameof(request.PlannedArrivalUtc)] = ["Planned arrival UTC is required."];
        if (request.PlannedDepartureUtc is null) errors[nameof(request.PlannedDepartureUtc)] = ["Planned departure UTC is required."];
        if (request.PlannedArrivalUtc is { } start && request.PlannedDepartureUtc is { } end && end <= start) errors[nameof(request.PlannedDepartureUtc)] = ["Planned departure UTC must be after planned arrival UTC."];
        if (string.IsNullOrWhiteSpace(request.Reason)) errors[nameof(request.Reason)] = ["Reason is required."];
        else if (request.Reason.Trim().Length > BerthAllocationChange.ReasonMaxLength) errors[nameof(request.Reason)] = [$"Reason must not exceed {BerthAllocationChange.ReasonMaxLength} characters."];
        return errors;
    }
}
