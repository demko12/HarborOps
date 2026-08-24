using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class SearchBerthsQuery(DbContext dbContext) : ISearchBerthsQuery
{
    public async Task<SearchBerthsResponse> SearchAsync(
        SearchBerthsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchBerthsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchBerthsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchBerthsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchBerthsValidator.DefaultPageSize;
        var portId = request.PortId!.Value;

        var berths = dbContext.Set<Berth>()
            .AsNoTracking()
            .Where(berth => berth.PortId == portId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            berths = berths.Where(berth => berth.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            berths = berths.Where(berth => berth.Status == status);
        }

        var totalCount = await berths.CountAsync(cancellationToken);

        var items = await berths
            .OrderBy(berth => berth.Name)
            .ThenBy(berth => berth.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(berth => new BerthListItem(
                berth.Id,
                berth.PortId,
                berth.Name,
                berth.Length,
                berth.MaxDraft,
                berth.Status))
            .ToListAsync(cancellationToken);

        return new SearchBerthsResponse(items, pageNumber, pageSize, totalCount);
    }
}
