using System.Text.Json;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ActivateCrewAssignmentService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task ActivateAsync(Guid assignmentId, Guid actingUserId, CancellationToken cancellationToken)
    {
        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        var assignment = await dbContext.Set<CrewAssignment>()
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken)
            ?? throw new CrewAssignmentNotFoundException(assignmentId);

        var unavailability = await CrewAvailabilityOverlap.FindApprovedUnavailabilityAsync(
            dbContext,
            assignment.CrewMemberId,
            assignment.SignOnDate,
            assignment.PlannedSignOffDate,
            cancellationToken);

        if (unavailability is { } hit)
        {
            throw new CrewMemberUnavailableException(
                assignment.CrewMemberId, hit.StartDate, hit.EndDate, "ApprovedUnavailability");
        }

        var policy = await dbContext.Set<CrewQualificationPolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CrewPositionId == assignment.RankId, cancellationToken);

        CrewQualificationCoverage.EvaluationResult? qualificationResult = null;

        if (policy is { Enabled: true })
        {
            var requiredTypes = await dbContext.Set<CrewQualificationRequirement>()
                .AsNoTracking()
                .Where(requirement => requirement.PolicyId == policy.Id)
                .Select(requirement => requirement.RequiredType)
                .ToListAsync(cancellationToken);

            qualificationResult = await CrewQualificationCoverage.EvaluateAsync(
                dbContext, assignment.CrewMemberId, requiredTypes, assignment.SignOnDate, assignment.PlannedSignOffDate, cancellationToken);

            if (!qualificationResult.IsFullyCovered)
            {
                throw new CrewMemberNotQualifiedException(
                    assignment.CrewMemberId, assignment.RankId, qualificationResult.MissingTypes, qualificationResult.UncoveredRanges);
            }
        }

        assignment.Activate();

        if (policy is not null && qualificationResult is not null)
        {
            var evidence = CrewAssignmentQualificationEvidence.Record(
                assignment.Id,
                policy.Id,
                policy.ConcurrencyToken,
                JsonSerializer.Serialize(qualificationResult.ContributingCertificateIds),
                actingUserId,
                timeProvider.GetUtcNow().UtcDateTime);

            await dbContext.Set<CrewAssignmentQualificationEvidence>().AddAsync(evidence, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
