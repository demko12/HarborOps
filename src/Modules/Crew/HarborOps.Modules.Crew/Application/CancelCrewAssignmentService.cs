using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CancelCrewAssignmentService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task CancelAsync(
        Guid assignmentId,
        Guid actingUserId,
        CancelCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assignment = await dbContext.Set<CrewAssignment>()
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken)
            ?? throw new CrewAssignmentNotFoundException(assignmentId);

        var errors = CrewLifecycleValidator.ValidateNotes(request.Notes, CrewAssignment.CancellationNotesMaxLength);
        if (errors.Count > 0)
        {
            throw new CrewLifecycleValidationException(errors);
        }

        assignment.Cancel(actingUserId, timeProvider.GetUtcNow().UtcDateTime, CrewLifecycleValidator.NormalizeNotes(request.Notes));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
