using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyageItineraryQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetVoyageItineraryQuery
{
    public async Task<VoyageItineraryResponse> GetItineraryAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        var vesselId = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .Where(voyage => voyage.Id == voyageId)
            .Select(voyage => (Guid?)voyage.VesselId)
            .SingleOrDefaultAsync(cancellationToken);

        if (vesselId is null)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var clearances = dbContext.Set<PortCallClearance>().AsNoTracking();
        var clearanceItems = dbContext.Set<PortCallClearanceItem>().AsNoTracking();

        var stops = await (
            from stop in dbContext.Set<VoyageStop>().AsNoTracking()
            join port in dbContext.Set<Port>().AsNoTracking()
                on stop.PortId equals port.Id
            join berth in dbContext.Set<Berth>().AsNoTracking()
                on stop.BerthId equals berth.Id into berthJoin
            from berth in berthJoin.DefaultIfEmpty()
            join clearance in clearances
                on stop.Id equals clearance.VoyageStopId into clearanceJoin
            from clearance in clearanceJoin.DefaultIfEmpty()
            where stop.VoyageId == voyageId
            orderby stop.StopOrder, stop.Id
            select new VoyageItineraryStopItem(
                stop.Id,
                stop.StopOrder,
                port.Id,
                port.Name,
                port.Country,
                port.UnLocode,
                stop.BerthId,
                berth != null ? berth.Name : null,
                stop.Eta,
                stop.Etd,
                stop.Ata,
                stop.Atd,
                stop.Status,
                port.Latitude,
                port.Longitude,
                clearance != null ? clearance.Status : null,
                clearance == null
                    ? 0
                    : clearanceItems.Count(item =>
                        item.PortCallClearanceId == clearance.Id && item.IsRequired),
                clearance == null
                    ? 0
                    : clearanceItems.Count(item =>
                        item.PortCallClearanceId == clearance.Id
                        && item.IsRequired
                        && item.Status != PortCallClearanceItem.PendingStatus),
                clearance == null
                    ? 0
                    : clearanceItems.Count(item =>
                        item.PortCallClearanceId == clearance.Id
                        && item.IsRequired
                        && item.Status == PortCallClearanceItem.PendingStatus
                        && item.DueAtUtc != null
                        && item.DueAtUtc < utcNow)))
            .ToListAsync(cancellationToken);

        var delays = await dbContext.Set<Delay>()
            .AsNoTracking()
            .Where(delay => delay.VoyageId == voyageId)
            .OrderBy(delay => delay.CreatedAt)
            .ThenBy(delay => delay.Id)
            .Select(delay => new VoyageDelayItem(
                delay.Id,
                delay.VoyageStopId,
                delay.Reason,
                delay.Description,
                delay.Duration,
                delay.CreatedAt))
            .ToListAsync(cancellationToken);

        return new VoyageItineraryResponse(voyageId, vesselId.Value, stops, delays);
    }
}
