using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class RouteLookupQuery(DbContext dbContext) : IRouteLookupQuery
{
    public async Task<RouteLookupResponse> LookupAsync(
        RouteLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RouteLookupValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RouteLookupValidationException(errors);
        }

        var take = request.Take ?? RouteLookupValidator.DefaultTake;
        var routes = dbContext.Set<Route>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            routes = routes.Where(route => route.Name.Contains(search));
        }

        var items = await routes
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Take(take)
            .Select(route => new RouteLookupItem(
                route.Id,
                route.Name,
                route.EstimatedDistance,
                route.EstimatedDuration))
            .ToListAsync(cancellationToken);

        return new RouteLookupResponse(items);
    }
}
