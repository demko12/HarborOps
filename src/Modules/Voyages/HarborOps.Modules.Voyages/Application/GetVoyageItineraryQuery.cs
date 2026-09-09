using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyageItineraryQuery(DbContext dbContext) : IGetVoyageItineraryQuery
{
    public async Task<VoyageItineraryResponse> GetItineraryAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        var voyageExists = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .AnyAsync(voyage => voyage.Id == voyageId, cancellationToken);

        if (!voyageExists)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        var stops = await (
            from stop in dbContext.Set<VoyageStop>().AsNoTracking()
            join port in dbContext.Set<Port>().AsNoTracking()
                on stop.PortId equals port.Id
            join berth in dbContext.Set<Berth>().AsNoTracking()
                on stop.BerthId equals berth.Id into berthJoin
            from berth in berthJoin.DefaultIfEmpty()
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
                port.Longitude))
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

        return new VoyageItineraryResponse(voyageId, stops, delays);
    }
}
