using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetBerthAllocationHistoryQuery(DbContext dbContext) : IGetBerthAllocationHistoryQuery
{
    public async Task<BerthAllocationHistoryResponse> GetAsync(Guid voyageId, Guid stopId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Set<VoyageStop>().AsNoTracking().AnyAsync(stop => stop.Id == stopId && stop.VoyageId == voyageId, cancellationToken);
        if (!exists) throw new VoyageStopNotFoundException(stopId);
        var berths = dbContext.Set<Berth>().AsNoTracking();
        var changes = await dbContext.Set<BerthAllocationChange>().AsNoTracking().Where(change => change.VoyageId == voyageId && change.VoyageStopId == stopId)
            .OrderByDescending(change => change.ChangedAtUtc).ThenByDescending(change => change.Id)
            .Select(change => new BerthAllocationHistoryItem(change.Id, change.PreviousBerthId, berths.Where(berth => berth.Id == change.PreviousBerthId).Select(berth => berth.Name).FirstOrDefault(), change.NewBerthId, berths.Where(berth => berth.Id == change.NewBerthId).Select(berth => berth.Name).FirstOrDefault()!, change.PreviousPlannedArrivalUtc, change.PreviousPlannedDepartureUtc, change.NewPlannedArrivalUtc, change.NewPlannedDepartureUtc, change.Reason, change.ChangedByUserId, change.ChangedAtUtc)).ToListAsync(cancellationToken);
        return new BerthAllocationHistoryResponse(voyageId, stopId, changes);
    }
}
