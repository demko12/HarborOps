using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ExecuteCrewRotationService(DbContext dbContext, IDateTimeProvider dateTimeProvider)
{
    public async Task<ExecuteCrewRotationResponse> ExecuteAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.Set<CrewAssignment>()
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken);

        if (assignment is null)
        {
            throw new CrewAssignmentNotFoundException(assignmentId);
        }

        var rotation = await dbContext.Set<CrewRotation>()
            .SingleOrDefaultAsync(candidate => candidate.AssignmentId == assignmentId, cancellationToken);

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

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var tourLengthDays = assignment.PlannedSignOffDate.DayNumber - assignment.SignOnDate.DayNumber;

        assignment.SignOff(today);

        var replacement = CrewAssignment.CreatePlanned(
            relievingCrewMemberId,
            assignment.VesselId,
            assignment.RankId,
            signOnPortId: null,
            today,
            today.AddDays(tourLengthDays));
        replacement.Activate();

        await dbContext.Set<CrewAssignment>().AddAsync(replacement, cancellationToken);
        dbContext.Set<CrewRotation>().Remove(rotation);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ExecuteCrewRotationResponse(
            assignment.Id,
            today,
            replacement.Id,
            today,
            replacement.PlannedSignOffDate);
    }
}
