using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class AmendVoyageScheduleService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<VoyageScheduleResponse> AmendAsync(
        Guid voyageId,
        Guid actingUserId,
        AmendVoyageScheduleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = AmendVoyageScheduleValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new AmendVoyageScheduleValidationException(errors);
        }

        var departurePortId = request.DeparturePortId!.Value;
        var arrivalPortId = request.ArrivalPortId!.Value;
        var plannedDepartureUtc = request.PlannedDepartureUtc!.Value;
        var plannedArrivalUtc = request.PlannedArrivalUtc!.Value;
        var reason = request.Reason!.Trim();

        var voyage = await dbContext.Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        await EnsurePortExistsAsync(departurePortId, cancellationToken);
        await EnsurePortExistsAsync(arrivalPortId, cancellationToken);

        if (request.RouteId is { } routeId)
        {
            await EnsureRouteConnectsPortsAsync(
                routeId,
                departurePortId,
                arrivalPortId,
                cancellationToken);
        }

        var previousRouteId = voyage.RouteId;
        var previousDeparturePortId = voyage.DeparturePortId;
        var previousArrivalPortId = voyage.ArrivalPortId;
        var previousPlannedDeparture = voyage.PlannedDeparture;
        var previousPlannedArrival = voyage.PlannedArrival;

        voyage.Amend(
            request.RouteId,
            departurePortId,
            arrivalPortId,
            plannedDepartureUtc,
            plannedArrivalUtc);

        var change = VoyageScheduleChange.RecordAmendment(
            voyage.Id,
            reason,
            actingUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            voyage.Status,
            previousRouteId,
            voyage.RouteId,
            previousDeparturePortId,
            departurePortId,
            previousArrivalPortId,
            arrivalPortId,
            previousPlannedDeparture,
            plannedDepartureUtc,
            previousPlannedArrival,
            plannedArrivalUtc);

        await dbContext.Set<VoyageScheduleChange>().AddAsync(change, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await VoyageScheduleProjection.ReadAsync(dbContext, voyageId, cancellationToken);
    }

    private async Task EnsurePortExistsAsync(Guid portId, CancellationToken cancellationToken)
    {
        var portExists = await dbContext.Set<Port>()
            .AsNoTracking()
            .AnyAsync(port => port.Id == portId, cancellationToken);

        if (!portExists)
        {
            throw new PortNotFoundException(portId);
        }
    }

    private async Task EnsureRouteConnectsPortsAsync(
        Guid routeId,
        Guid departurePortId,
        Guid arrivalPortId,
        CancellationToken cancellationToken)
    {
        var routeExists = await dbContext.Set<Route>()
            .AsNoTracking()
            .AnyAsync(route => route.Id == routeId, cancellationToken);

        if (!routeExists)
        {
            throw new RouteNotFoundException(routeId);
        }

        var routePortIds = await dbContext.Set<RoutePort>()
            .AsNoTracking()
            .Where(routePort => routePort.RouteId == routeId)
            .OrderBy(routePort => routePort.StopOrder)
            .Select(routePort => routePort.PortId)
            .ToListAsync(cancellationToken);

        if (routePortIds.Count == 0
            || routePortIds[0] != departurePortId
            || routePortIds[^1] != arrivalPortId)
        {
            throw new VoyageRouteEndpointMismatchException(
                routeId,
                departurePortId,
                arrivalPortId);
        }
    }
}
