using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewEmploymentStatusHistoryQuery(DbContext dbContext) : IGetCrewEmploymentStatusHistoryQuery
{
    public async Task<GetCrewEmploymentStatusHistoryResponse> GetAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        var crewMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var items = await dbContext.Set<CrewEmploymentStatusHistory>()
            .AsNoTracking()
            .Where(history => history.CrewMemberId == crewMemberId)
            .OrderByDescending(history => history.ChangedAtUtc)
            .ThenByDescending(history => history.Id)
            .Select(history => new CrewEmploymentStatusHistoryItem(
                history.Id,
                history.Status,
                history.ChangedByUserId,
                history.Notes,
                history.ChangedAtUtc))
            .ToListAsync(cancellationToken);

        return new GetCrewEmploymentStatusHistoryResponse(items);
    }
}
