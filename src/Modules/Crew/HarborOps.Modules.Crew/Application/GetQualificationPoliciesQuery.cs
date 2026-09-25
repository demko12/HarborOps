using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetQualificationPoliciesQuery(DbContext dbContext) : IGetQualificationPoliciesQuery
{
    public async Task<GetQualificationPoliciesResponse> GetAsync(
        GetQualificationPoliciesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetQualificationPoliciesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetQualificationPoliciesValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetQualificationPoliciesValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetQualificationPoliciesValidator.DefaultPageSize;

        var rows =
            from policy in dbContext.Set<CrewQualificationPolicy>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on policy.CrewPositionId equals position.Id
            select new
            {
                policy.Id,
                policy.CrewPositionId,
                position.RankName,
                policy.Enabled,
                policy.ConcurrencyToken,
                RequiredTypeCount = dbContext.Set<CrewQualificationRequirement>().Count(requirement => requirement.PolicyId == policy.Id),
            };

        if (request.CrewPositionId is { } crewPositionId)
        {
            rows = rows.Where(row => row.CrewPositionId == crewPositionId);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.RankName)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new QualificationPolicyListItem(
                row.Id, row.CrewPositionId, row.RankName, row.Enabled, row.RequiredTypeCount, row.ConcurrencyToken))
            .ToListAsync(cancellationToken);

        return new GetQualificationPoliciesResponse(items, pageNumber, pageSize, totalCount);
    }
}
