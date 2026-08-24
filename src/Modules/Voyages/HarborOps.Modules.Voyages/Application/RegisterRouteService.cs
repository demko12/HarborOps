using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RegisterRouteService(DbContext dbContext)
{
    public async Task<RegisterRouteResponse> RegisterAsync(
        RegisterRouteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterRouteValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterRouteValidationException(errors);
        }

        var orderedStops = RegisterRouteValidator.OrderStops(request.Stops!);
        var portIds = orderedStops.Select(stop => stop.PortId!.Value).ToArray();

        var knownPortIds = await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(port => portIds.Contains(port.Id))
            .Select(port => port.Id)
            .ToListAsync(cancellationToken);

        var missingPortIds = portIds
            .Where(portId => !knownPortIds.Contains(portId))
            .ToArray();

        if (missingPortIds.Length > 0)
        {
            throw new RouteStopPortNotFoundException(missingPortIds[0]);
        }

        var route = Route.Create(
            request.Name!.Trim(),
            request.EstimatedDistance!.Value,
            request.EstimatedDuration!.Value,
            Normalize(request.Description));

        var routePorts = orderedStops
            .Select(stop => RoutePort.Create(
                route.Id,
                stop.PortId!.Value,
                stop.StopOrder!.Value,
                stop.DistanceFromPrevious!.Value,
                stop.EstimatedDurationFromPrevious!.Value))
            .ToList();

        await dbContext.Set<Route>().AddAsync(route, cancellationToken);
        await dbContext.Set<RoutePort>().AddRangeAsync(routePorts, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterRouteResponse(route.Id);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
