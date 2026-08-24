using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class SearchPortsQuery(DbContext dbContext) : ISearchPortsQuery
{
    public async Task<SearchPortsResponse> SearchAsync(
        SearchPortsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchPortsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchPortsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchPortsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchPortsValidator.DefaultPageSize;

        var ports = dbContext.Set<Port>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            ports = ports.Where(port =>
                port.Name.Contains(search)
                || (port.City != null && port.City.Contains(search))
                || (port.UnLocode != null && port.UnLocode.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var country = request.Country.Trim();
            ports = ports.Where(port => port.Country == country);
        }

        var totalCount = await ports.CountAsync(cancellationToken);

        var items = await ports
            .OrderBy(port => port.Name)
            .ThenBy(port => port.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(port => new PortListItem(
                port.Id,
                port.Name,
                port.Country,
                port.City,
                port.UnLocode,
                port.Latitude,
                port.Longitude))
            .ToListAsync(cancellationToken);

        return new SearchPortsResponse(items, pageNumber, pageSize, totalCount);
    }
}
