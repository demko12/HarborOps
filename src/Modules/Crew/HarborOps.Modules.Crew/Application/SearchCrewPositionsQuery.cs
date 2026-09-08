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

        var pageNumber = request.PageNumber ?? 1;
        var pageSize = request.PageSize ?? SearchCrewPositionsValidator.DefaultPageSize;

        var positions = dbContext.Set<CrewPosition>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            positions = positions.Where(position => position.RankName.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var department = request.Department.Trim();
            positions = positions.Where(position => position.Department == department);
        }

        var totalCount = await positions.CountAsync(cancellationToken);

        var items = await positions
            .OrderBy(position => position.Department)
            .ThenBy(position => position.RankName)
            .ThenBy(position => position.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(position => new CrewPositionListItem(
                position.Id,
                position.RankName,
                position.Department,
                position.SeniorityLevel,
                dbContext.Set<CrewMember>().Count(member => member.BaseRankId == position.Id),
                dbContext.Set<CrewAssignment>().Count(assignment => assignment.RankId == position.Id)))
            .ToListAsync(cancellationToken);

        return new SearchCrewPositionsResponse(items, pageNumber, pageSize, totalCount);
    }
}
