using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class DeleteCrewAssignmentService(DbContext dbContext)
{
    public async Task DeleteAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var crewAssignments = dbContext.Set<CrewAssignment>();

        var assignment = await crewAssignments
            .SingleOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);

        if (assignment is null)
        {
            throw new CrewAssignmentNotFoundException(assignmentId);
        }

        if (assignment.Status != CrewAssignment.PlannedStatus)
        {
            throw new CrewAssignmentNotDeletableException(assignmentId);
        }

        crewAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
