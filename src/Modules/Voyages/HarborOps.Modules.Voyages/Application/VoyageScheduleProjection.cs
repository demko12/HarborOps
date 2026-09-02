using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal static class VoyageScheduleProjection
{
    internal static async Task<VoyageScheduleResponse> ReadAsync(
        DbContext dbContext,
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        var routes = dbContext.Set<Route>().AsNoTracking();
        var ports = dbContext.Set<Port>().AsNoTracking();

        var summary = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .Where(voyage => voyage.Id == voyageId)
            .Select(voyage => new VoyageScheduleSummary(
                voyage.Id,
                voyage.VoyageNumber,
                voyage.Status,
                voyage.RouteId,
                routes.Where(route => route.Id == voyage.RouteId)
                    .Select(route => route.Name)
                    .FirstOrDefault(),
                voyage.DeparturePortId,
                ports.Where(port => port.Id == voyage.DeparturePortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                voyage.ArrivalPortId,
                ports.Where(port => port.Id == voyage.ArrivalPortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                voyage.PlannedDeparture,
                voyage.PlannedArrival))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var changes = await dbContext.Set<VoyageScheduleChange>()
            .AsNoTracking()
            .Where(change => change.VoyageId == voyageId)
            .OrderByDescending(change => change.ChangedAtUtc)
            .ThenByDescending(change => change.Id)
            .Select(change => new VoyageScheduleChangeItem(
                change.Id,
                change.ChangeType,
                change.Reason,
                change.ChangedByUserId,
                change.ChangedAtUtc,
                change.PreviousStatus,
                change.NewStatus,
                change.PreviousRouteId,
                routes.Where(route => route.Id == change.PreviousRouteId)
                    .Select(route => route.Name)
                    .FirstOrDefault(),
                change.NewRouteId,
                routes.Where(route => route.Id == change.NewRouteId)
                    .Select(route => route.Name)
                    .FirstOrDefault(),
                change.PreviousDeparturePortId,
                ports.Where(port => port.Id == change.PreviousDeparturePortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                change.NewDeparturePortId,
                ports.Where(port => port.Id == change.NewDeparturePortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                change.PreviousArrivalPortId,
                ports.Where(port => port.Id == change.PreviousArrivalPortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                change.NewArrivalPortId,
                ports.Where(port => port.Id == change.NewArrivalPortId)
                    .Select(port => port.Name)
                    .FirstOrDefault(),
                change.PreviousPlannedDeparture,
                change.NewPlannedDeparture,
                change.PreviousPlannedArrival,
                change.NewPlannedArrival))
            .ToListAsync(cancellationToken);

        return new VoyageScheduleResponse(summary, changes);
    }
}
