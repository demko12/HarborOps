using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class SearchCrewMembersQuery(DbContext dbContext) : ISearchCrewMembersQuery
{
    public async Task<SearchCrewMembersResponse> SearchAsync(
        SearchCrewMembersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchCrewMembersValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchCrewMembersValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchCrewMembersValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchCrewMembersValidator.DefaultPageSize;

        var rows =
            from member in dbContext.Set<CrewMember>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on member.BaseRankId equals position.Id
            select new { Member = member, BaseRankName = position.RankName };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            rows = rows.Where(row =>
                row.Member.FirstName.Contains(search)
                || row.Member.LastName.Contains(search)
                || row.Member.ImoSeafarerId == search);
        }

        if (!string.IsNullOrWhiteSpace(request.EmploymentStatus))
        {
            var employmentStatus = request.EmploymentStatus.Trim();
            rows = rows.Where(row => row.Member.EmploymentStatus == employmentStatus);
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderBy(row => row.Member.PassportExpiry)
            .ThenBy(row => row.Member.FirstName)
            .ThenBy(row => row.Member.LastName)
            .ThenBy(row => row.Member.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new CrewMemberListItem(
                row.Member.Id,
                row.Member.FirstName + " " + row.Member.LastName,
                row.Member.Nationality,
                row.Member.ImoSeafarerId,
                row.Member.BaseRankId,
                row.BaseRankName,
                row.Member.EmploymentStatus,
                row.Member.PassportExpiry,
                row.Member.HireDate))
            .ToListAsync(cancellationToken);

        return new SearchCrewMembersResponse(items, pageNumber, pageSize, totalCount);
    }
}
