using System.Text.Json;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetQualificationPolicyDetailQuery(DbContext dbContext) : IGetQualificationPolicyDetailQuery
{
    public async Task<GetQualificationPolicyDetailResponse> GetAsync(
        Guid policyId,
        CancellationToken cancellationToken)
    {
        var policy = await (
            from candidate in dbContext.Set<CrewQualificationPolicy>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on candidate.CrewPositionId equals position.Id
            where candidate.Id == policyId
            select new { candidate.Id, candidate.CrewPositionId, position.RankName, candidate.Enabled, candidate.ConcurrencyToken })
            .FirstOrDefaultAsync(cancellationToken);

        if (policy is null)
        {
            throw new CrewQualificationPolicyNotFoundException(policyId);
        }

        var requiredTypes = await dbContext.Set<CrewQualificationRequirement>()
            .AsNoTracking()
            .Where(requirement => requirement.PolicyId == policyId)
            .OrderBy(requirement => requirement.RequiredType)
            .Select(requirement => requirement.RequiredType)
            .ToListAsync(cancellationToken);

        var historyEntries = await dbContext.Set<CrewQualificationPolicyHistory>()
            .AsNoTracking()
            .Where(entry => entry.PolicyId == policyId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var history = historyEntries
            .Select(entry => new QualificationPolicyHistoryItem(
                entry.Id,
                entry.EventType,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.PreviousEnabled,
                entry.NewEnabled,
                DeserializeTypes(entry.PreviousRequiredTypesSnapshot),
                DeserializeTypes(entry.NewRequiredTypesSnapshot)))
            .ToList();

        return new GetQualificationPolicyDetailResponse(
            policy.Id, policy.CrewPositionId, policy.RankName, policy.Enabled, policy.ConcurrencyToken, requiredTypes, history);
    }

    private static IReadOnlyList<string>? DeserializeTypes(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<List<string>>(json);
}
