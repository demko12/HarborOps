using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class PlanVoyageItineraryService(DbContext dbContext)
{
    public async Task<PlanVoyageItineraryResponse> PlanAsync(
        Guid voyageId,
        PlanVoyageItineraryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shapeErrors = PlanVoyageItineraryValidator.ValidateShape(request);
        if (shapeErrors.Count > 0)
        {
            throw new PlanVoyageItineraryValidationException(shapeErrors);
        }

        var orderedStops = PlanVoyageItineraryValidator.OrderStops(request.Stops!);

        var voyage = await dbContext.Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        if (!string.Equals(voyage.Status, Voyage.PlannedStatus, StringComparison.Ordinal))
        {
            throw new VoyageItineraryNotPlannableException(voyageId, voyage.Status);
        }

        var hasExistingItinerary = await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .AnyAsync(stop => stop.VoyageId == voyageId, cancellationToken);

        if (hasExistingItinerary)
        {
            throw new VoyageItineraryAlreadyPlannedException(voyageId);
        }

        int? routeStopCount = null;
        IReadOnlyList<RoutePort> routePorts = [];

        if (request.RouteId is { } routeId)
        {
            var routeExists = await dbContext.Set<Route>()
                .AsNoTracking()
                .AnyAsync(route => route.Id == routeId, cancellationToken);

            if (!routeExists)
            {
                throw new RouteNotFoundException(routeId);
            }

            routePorts = await dbContext.Set<RoutePort>()
                .AsNoTracking()
                .Where(routePort => routePort.RouteId == routeId)
                .OrderBy(routePort => routePort.StopOrder)
                .ToListAsync(cancellationToken);

            routeStopCount = routePorts.Count;
        }

        var sourcingErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        PlanVoyageItineraryValidator.ValidateStopSourcing(orderedStops, routeStopCount, sourcingErrors);
        if (sourcingErrors.Count > 0)
        {
            throw new PlanVoyageItineraryValidationException(sourcingErrors);
        }

        var explicitPortIds = orderedStops
            .Where(stop => routeStopCount is not { } count || stop.StopOrder!.Value > count)
            .Select(stop => stop.PortId!.Value)
            .ToArray();

        if (explicitPortIds.Length > 0)
        {
            var knownPortIds = await dbContext.Set<Port>()
                .AsNoTracking()
                .Where(port => explicitPortIds.Contains(port.Id))
                .Select(port => port.Id)
                .ToListAsync(cancellationToken);

            var missingPortIds = explicitPortIds
                .Where(portId => !knownPortIds.Contains(portId))
                .ToArray();

            if (missingPortIds.Length > 0)
            {
                throw new VoyageStopPortNotFoundException(missingPortIds[0]);
            }
        }

        var stops = new List<VoyageStop>(orderedStops.Count);

        for (var index = 0; index < orderedStops.Count; index++)
        {
            var requested = orderedStops[index];
            var isRouteCovered = routeStopCount is { } count && requested.StopOrder!.Value <= count;
            var portId = isRouteCovered ? routePorts[index].PortId : requested.PortId!.Value;

            stops.Add(VoyageStop.Create(
                voyageId,
                portId,
                requested.StopOrder!.Value,
                requested.Eta!.Value,
                requested.Etd!.Value));
        }

        await dbContext.Set<VoyageStop>().AddRangeAsync(stops, cancellationToken);

        var legs = new List<VoyageLeg>(stops.Count - 1);

        for (var index = 1; index < stops.Count; index++)
        {
            var requested = orderedStops[index];
            var isRouteCovered = routeStopCount is { } count && requested.StopOrder!.Value <= count;
            var distance = isRouteCovered
                ? routePorts[index].DistanceFromPrevious
                : requested.DistanceFromPrevious!.Value;
            var duration = isRouteCovered
                ? routePorts[index].EstimatedDurationFromPrevious
                : requested.EstimatedDurationFromPrevious!.Value;

            legs.Add(VoyageLeg.Create(voyageId, stops[index - 1].Id, stops[index].Id, distance, duration));
        }

        await dbContext.Set<VoyageLeg>().AddRangeAsync(legs, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PlanVoyageItineraryResponse(
            voyageId,
            stops
                .Select(stop => new PlannedVoyageStopItem(
                    stop.Id,
                    stop.StopOrder,
                    stop.PortId,
                    stop.Eta,
                    stop.Etd))
                .ToList());
    }
}
