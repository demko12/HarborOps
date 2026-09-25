using System.Text.Json;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CreateQualificationPolicyService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CreateQualificationPolicyResponse> CreateAsync(
        Guid createdByUserId,
        CreateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreateQualificationPolicyValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreateQualificationPolicyValidationException(errors);
        }

        var crewPositionId = request.CrewPositionId!.Value;

        var positionExists = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .AnyAsync(position => position.Id == crewPositionId, cancellationToken);

        if (!positionExists)
        {
            throw new CrewPositionNotFoundException(crewPositionId);
        }

        var policyExists = await dbContext.Set<CrewQualificationPolicy>()
            .AsNoTracking()
            .AnyAsync(policy => policy.CrewPositionId == crewPositionId, cancellationToken);

        if (policyExists)
        {
            throw new DuplicateCrewQualificationPolicyException(crewPositionId);
        }

        var enabled = request.Enabled!.Value;
        var requiredTypes = request.Requirements!
            .Select(requirement => requirement.RequiredType!.Trim())
            .ToList();
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var policy = CrewQualificationPolicy.Create(crewPositionId, enabled, createdByUserId, occurredAtUtc);
        var requirements = requiredTypes
            .Select(type => CrewQualificationRequirement.Create(policy.Id, type))
            .ToList();

        var history = CrewQualificationPolicyHistory.RecordCreated(
            policy.Id, createdByUserId, occurredAtUtc, enabled, JsonSerializer.Serialize(requiredTypes.OrderBy(t => t, StringComparer.Ordinal)));

        await dbContext.Set<CrewQualificationPolicy>().AddAsync(policy, cancellationToken);
        await dbContext.Set<CrewQualificationRequirement>().AddRangeAsync(requirements, cancellationToken);
        await dbContext.Set<CrewQualificationPolicyHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateQualificationPolicyResponse(
            policy.Id, policy.CrewPositionId, policy.Enabled, policy.ConcurrencyToken, requiredTypes);
    }
}
