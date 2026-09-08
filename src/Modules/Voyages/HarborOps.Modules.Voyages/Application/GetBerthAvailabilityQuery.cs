using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetBerthAvailabilityQuery(DbContext dbContext) : IGetBerthAvailabilityQuery
{
    public async Task<BerthAvailabilityResponse> GetAsync(Guid portId, BerthAvailabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = BerthAvailabilityValidator.Validate(request);
        if (errors.Count > 0) throw new BerthAvailabilityValidationException(errors);
        var from = request.FromUtc!.Value;
        var to = request.ToUtc!.Value;
        var horizonEnd = from.AddDays(BerthAvailabilityValidator.MaxSearchDays);
        var portExists = await dbContext.Set<Port>().AsNoTracking().AnyAsync(port => port.Id == portId, cancellationToken);
        if (!portExists) throw new PortNotFoundException(portId);

        var berths = await dbContext.Set<Berth>().AsNoTracking()
            .Where(berth => berth.PortId == portId && berth.Status == "Active"
                && (request.Draft == null || berth.MaxDraft >= request.Draft)
                && (request.Length == null || berth.Length >= request.Length))
            .OrderBy(berth => berth.Name).ThenBy(berth => berth.Id)
            .Select(berth => new AvailabilityBerth(berth.Id, berth.Name, berth.Length, berth.MaxDraft))
            .ToListAsync(cancellationToken);
        var berthIds = berths.Select(berth => berth.Id).ToArray();
        var schedules = await dbContext.Set<BerthSchedule>().AsNoTracking()
            .Where(schedule => berthIds.Contains(schedule.BerthId)
                && BerthSchedule.ActiveStatuses.Contains(schedule.Status)
                && schedule.StartTime < horizonEnd && from < schedule.EndTime)
            .OrderBy(schedule => schedule.StartTime).ThenBy(schedule => schedule.EndTime).ThenBy(schedule => schedule.Id)
            .Select(schedule => new AvailabilitySchedule(schedule.BerthId, schedule.StartTime, schedule.EndTime))
            .ToListAsync(cancellationToken);
        var items = berths.Select(berth => CreateItem(berth, schedules.Where(s => s.BerthId == berth.Id), from, to, horizonEnd)).ToList();
        return new BerthAvailabilityResponse(portId, from, to, horizonEnd, items);
    }

    private static BerthAvailabilityItem CreateItem(AvailabilityBerth berth, IEnumerable<AvailabilitySchedule> entries, DateTime from, DateTime to, DateTime horizonEnd)
    {
        var schedules = entries.ToList();
        var requestedAvailable = !schedules.Any(schedule => schedule.Start < to && from < schedule.End);
        var duration = to - from;
        var candidate = from;
        foreach (var schedule in schedules)
        {
            if (candidate.Add(duration) <= schedule.Start) break;
            if (schedule.End > candidate) candidate = schedule.End;
        }
        var nearestFrom = candidate.Add(duration) <= horizonEnd ? candidate : (DateTime?)null;
        return new BerthAvailabilityItem(berth.Id, berth.Name, berth.Length, berth.MaxDraft, requestedAvailable, nearestFrom, nearestFrom?.Add(duration));
    }

    private sealed record AvailabilityBerth(Guid Id, string Name, decimal Length, decimal MaxDraft);
    private sealed record AvailabilitySchedule(Guid BerthId, DateTime Start, DateTime End);
}
