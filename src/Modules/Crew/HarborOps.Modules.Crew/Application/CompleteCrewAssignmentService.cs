using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CompleteCrewAssignmentService(DbContext dbContext)
{
    public async Task CompleteAsync(
        Guid assignmentId,
        CompleteCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assignment = await dbContext.Set<CrewAssignment>()
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken)
            ?? throw new CrewAssignmentNotFoundException(assignmentId);

        var errors = CrewLifecycleValidator.ValidateCompletion(
            request.ActualSignOffDate,
            request.SignOffPortId,
            assignment.SignOnDate);
        if (errors.Count > 0)
        {
            throw new CrewLifecycleValidationException(errors);
        }

        assignment.Complete(request.ActualSignOffDate!.Value, request.SignOffPortId);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
