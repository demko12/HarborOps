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

        await CrewRotationEligibility.EnsureRelievingCrewMemberAvailableAsync(
            dbContext, assignment.CrewMemberId, relievingCrewMemberId, cancellationToken);

        var executedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(executedAtUtc);
        var tourLengthDays = assignment.PlannedSignOffDate.DayNumber - assignment.SignOnDate.DayNumber;

        var replacement = CrewAssignment.CreatePlanned(
            relievingCrewMemberId,
            assignment.VesselId,
            assignment.RankId,
            signOnPortId: null,
            today,
            today.AddDays(tourLengthDays));
        replacement.Activate();

        assignment.Complete(today, signOffPortId: null);
        rotation.Execute(actingUserId, executedAtUtc, replacement.Id);
        await dbContext.Set<CrewAssignment>().AddAsync(replacement, cancellationToken);

        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ExecuteCrewRotationResponse(
            assignment.Id,
            today,
            replacement.Id,
            today,
            replacement.PlannedSignOffDate);
    }
}
