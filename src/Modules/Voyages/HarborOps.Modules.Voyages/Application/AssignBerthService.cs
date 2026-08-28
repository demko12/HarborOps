using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class AssignBerthService(DbContext dbContext)
{
    public async Task<AssignBerthToStopResponse> AssignAsync(
        Guid voyageId,
        Guid stopId,
        AssignBerthToStopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = AssignBerthValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new AssignBerthValidationException(errors);
        }

        var berthId = request.BerthId!.Value;

        var stop = await dbContext.Set<VoyageStop>()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == stopId && candidate.VoyageId == voyageId,
                cancellationToken)
            ?? throw new VoyageStopNotFoundException(stopId);

        var berth = await dbContext.Set<Berth>()
            .FirstOrDefaultAsync(candidate => candidate.Id == berthId, cancellationToken)
            ?? throw new BerthNotFoundException(berthId);

        if (berth.PortId != stop.PortId)
        {
            throw new BerthPortMismatchException(berthId, stop.PortId);
        }

        var overlaps = await dbContext.Set<BerthSchedule>()
            .Where(schedule => schedule.BerthId == berthId
                && schedule.VoyageStopId != stopId
                && BerthSchedule.ActiveStatuses.Contains(schedule.Status)
                && schedule.StartTime < stop.Etd
                && stop.Eta < schedule.EndTime)
            .AnyAsync(cancellationToken);

        if (overlaps)
        {
            throw new BerthOverlapException(berthId, stop.Eta, stop.Etd);
        }

        var existingSchedule = await dbContext.Set<BerthSchedule>()
            .Where(schedule => schedule.VoyageStopId == stopId
                && BerthSchedule.ActiveStatuses.Contains(schedule.Status))
            .SingleOrDefaultAsync(cancellationToken);

        existingSchedule?.Release();

        var newSchedule = BerthSchedule.Create(berthId, stopId, stop.Eta, stop.Etd);

        await dbContext.Set<BerthSchedule>().AddAsync(newSchedule, cancellationToken);

        stop.AssignBerth(berthId);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignBerthToStopResponse(
            stop.Id,
            berthId,
            newSchedule.StartTime,
            newSchedule.EndTime,
            newSchedule.Status);
    }
}
