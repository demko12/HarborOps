using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetRouteDetailsQuery(DbContext dbContext) : IGetRouteDetailsQuery
{
    public async Task<RouteDetailsResponse> GetRouteDetailsAsync(
        Guid routeId,
        CancellationToken cancellationToken)
    {
        var route = await dbContext.Set<Route>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == routeId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.Description,
                candidate.EstimatedDistance,
                candidate.EstimatedDuration,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new RouteNotFoundException(routeId);

        var stops = await (
            from routePort in dbContext.Set<RoutePort>().AsNoTracking()
            join port in dbContext.Set<Port>().AsNoTracking()
                on routePort.PortId equals port.Id
            where routePort.RouteId == routeId
            orderby routePort.StopOrder, port.Id
            select new RouteStopItem(
                port.Id,
                port.Name,
                port.Country,
                port.UnLocode,
                routePort.StopOrder,
                routePort.DistanceFromPrevious,
                routePort.EstimatedDurationFromPrevious))
            .ToListAsync(cancellationToken);

        return new RouteDetailsResponse(
            route.Id,
            route.Name,
            route.Description,
            route.EstimatedDistance,
            route.EstimatedDuration,
            stops);
    }
}
