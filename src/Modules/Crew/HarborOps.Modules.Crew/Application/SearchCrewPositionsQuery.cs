using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class SearchCrewPositionsQuery(DbContext dbContext) : ISearchCrewPositionsQuery
{
    public async Task<SearchCrewPositionsResponse> SearchAsync(
        SearchCrewPositionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchCrewPositionsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchCrewPositionsValidationException(errors);
        }

        var pageSize = request.PageSize ?? SearchCrewPositionsValidator.DefaultPageSize;

        var positions = dbContext.Set<CrewPosition>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            positions = positions.Where(position => position.RankName.Contains(search));
        }

        var items = await positions
            .OrderBy(position => position.RankName)
            .ThenBy(position => position.Id)
            .Take(pageSize)
            .Select(position => new CrewPositionListItem(
                position.Id,
                position.RankName,
                position.Department,
                position.SeniorityLevel))
            .ToListAsync(cancellationToken);

        return new SearchCrewPositionsResponse(items);
    }
}
