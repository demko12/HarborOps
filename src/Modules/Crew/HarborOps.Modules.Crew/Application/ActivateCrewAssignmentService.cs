using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ActivateCrewAssignmentService(DbContext dbContext)
{
    public async Task ActivateAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
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

        assignment.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
