using System.Text.Json;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ExecuteCrewRotationService(DbContext dbContext, TimeProvider timeProvider)
{
    public Task<ExecuteCrewRotationResponse> ExecuteAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        ExecuteAsync(assignmentId, Guid.Empty, cancellationToken);

    public async Task<ExecuteCrewRotationResponse> ExecuteAsync(
        Guid assignmentId,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;

        var assignment = await dbContext.Set<CrewAssignment>()
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken);

        if (assignment is null)
        {
            throw new CrewAssignmentNotFoundException(assignmentId);
        }

        var rotation = await dbContext.Set<CrewRotation>()
            .SingleOrDefaultAsync(
                candidate => candidate.AssignmentId == assignmentId
                    && candidate.Status == CrewRotation.PlannedStatus,
                cancellationToken);

        if (rotation is null)
        {
            throw new CrewRotationNotFoundException(assignmentId);
        }

        if (rotation.RelievingCrewMemberId is not { } relievingCrewMemberId)
        {
            throw new CrewRotationNotExecutableException(assignmentId);
        }

        if (!CrewRotationEligibility.IsAssignmentEligibleForRotation(assignment))
        {
            throw new CrewAssignmentNotEligibleForRotationException(assignmentId);
        }

        var executedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(executedAtUtc);
        var tourLengthDays = assignment.PlannedSignOffDate.DayNumber - assignment.SignOnDate.DayNumber;
        var replacementSignOnDate = today;
        var replacementPlannedSignOffDate = today.AddDays(tourLengthDays);

        // The replacement assignment runs today .. today + tour length, so the relieving member must
        // be free (no open assignment and no approved unavailability) across that whole window.
        await CrewRotationEligibility.EnsureRelievingCrewMemberAvailableAsync(
            dbContext,
            assignment.CrewMemberId,
            relievingCrewMemberId,
            replacementSignOnDate,
            replacementPlannedSignOffDate,
            cancellationToken);

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
                dbContext, relievingCrewMemberId, requiredTypes, replacementSignOnDate, replacementPlannedSignOffDate, cancellationToken);

            if (!qualificationResult.IsFullyCovered)
            {
                throw new CrewMemberNotQualifiedException(
                    relievingCrewMemberId, assignment.RankId, qualificationResult.MissingTypes, qualificationResult.UncoveredRanges);
            }
        }

        var replacement = CrewAssignment.CreatePlanned(
            relievingCrewMemberId,
            assignment.VesselId,
            assignment.RankId,
            signOnPortId: null,
            replacementSignOnDate,
            replacementPlannedSignOffDate);
        replacement.Activate();

        assignment.Complete(today, signOffPortId: null);
        rotation.Execute(actingUserId, executedAtUtc, replacement.Id);
        await dbContext.Set<CrewAssignment>().AddAsync(replacement, cancellationToken);

        if (policy is not null && qualificationResult is not null)
        {
            var evidence = CrewAssignmentQualificationEvidence.Record(
                replacement.Id,
                policy.Id,
                policy.ConcurrencyToken,
                JsonSerializer.Serialize(qualificationResult.ContributingCertificateIds),
                actingUserId,
                executedAtUtc);

            await dbContext.Set<CrewAssignmentQualificationEvidence>().AddAsync(evidence, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ExecuteCrewRotationResponse(
            assignment.Id,
            today,
            replacement.Id,
            replacementSignOnDate,
            replacement.PlannedSignOffDate);
    }
}
