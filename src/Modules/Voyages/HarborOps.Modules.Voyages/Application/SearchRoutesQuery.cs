using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class SearchRoutesQuery(DbContext dbContext) : ISearchRoutesQuery
{
    public async Task<SearchRoutesResponse> SearchAsync(
        SearchRoutesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchRoutesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchRoutesValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchRoutesValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchRoutesValidator.DefaultPageSize;

        var routePorts = dbContext.Set<RoutePort>().AsNoTracking();
        var routes = dbContext.Set<Route>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            routes = routes.Where(route => route.Name.Contains(search));
        }

        var totalCount = await routes.CountAsync(cancellationToken);

        var items = await routes
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(route => new RouteListItem(
                route.Id,
                route.Name,
                route.Description,
                route.EstimatedDistance,
                route.EstimatedDuration,
                routePorts.Count(routePort => routePort.RouteId == route.Id)))
            .ToListAsync(cancellationToken);

        return new SearchRoutesResponse(items, pageNumber, pageSize, totalCount);
    }
}
