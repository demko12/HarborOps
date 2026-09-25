using System.Text.Json;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class UpdateQualificationPolicyService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<UpdateQualificationPolicyResponse> UpdateAsync(
        Guid policyId,
        Guid actorUserId,
        UpdateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateQualificationPolicyValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateQualificationPolicyValidationException(errors);
        }

        var requestedTypes = request.Requirements!
            .Select(requirement => requirement.RequiredType!.Trim())
            .ToList();
        var newEnabled = request.Enabled!.Value;

        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        var policy = await dbContext.Set<CrewQualificationPolicy>()
            .FirstOrDefaultAsync(candidate => candidate.Id == policyId, cancellationToken);

        if (policy is null)
        {
            throw new CrewQualificationPolicyNotFoundException(policyId);
        }

        var existingRequirements = await dbContext.Set<CrewQualificationRequirement>()
            .Where(requirement => requirement.PolicyId == policyId)
            .ToListAsync(cancellationToken);

        var previousEnabled = policy.Enabled;
        var previousTypesSorted = existingRequirements
            .Select(requirement => requirement.RequiredType)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

        var requestedSet = new HashSet<string>(requestedTypes, StringComparer.OrdinalIgnoreCase);
        var existingTypeSet = new HashSet<string>(
            existingRequirements.Select(requirement => requirement.RequiredType), StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingRequirements)
        {
            if (!requestedSet.Contains(existing.RequiredType))
            {
                dbContext.Set<CrewQualificationRequirement>().Remove(existing);
            }
        }

        foreach (var type in requestedTypes)
        {
            if (!existingTypeSet.Contains(type))
            {
                await dbContext.Set<CrewQualificationRequirement>().AddAsync(
                    CrewQualificationRequirement.Create(policyId, type), cancellationToken);
            }
        }

        if (newEnabled != previousEnabled)
        {
            policy.Update(newEnabled);
        }
        else
        {
            policy.Touch();
        }

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var newTypesSorted = requestedTypes.OrderBy(type => type, StringComparer.Ordinal).ToList();

        var history = CrewQualificationPolicyHistory.RecordUpdated(
            policyId,
            actorUserId,
            occurredAtUtc,
            previousEnabled,
            newEnabled,
            JsonSerializer.Serialize(previousTypesSorted),
            JsonSerializer.Serialize(newTypesSorted));

        await dbContext.Set<CrewQualificationPolicyHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var finalRequiredTypes = await dbContext.Set<CrewQualificationRequirement>()
            .AsNoTracking()
            .Where(requirement => requirement.PolicyId == policyId)
            .Select(requirement => requirement.RequiredType)
            .ToListAsync(cancellationToken);

        return new UpdateQualificationPolicyResponse(policy.Id, policy.Enabled, policy.ConcurrencyToken, finalRequiredTypes);
    }
}
