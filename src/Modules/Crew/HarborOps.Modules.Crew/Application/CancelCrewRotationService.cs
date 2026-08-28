using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CancelCrewRotationService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task CancelAsync(
        Guid rotationId,
        Guid actingUserId,
        CancelCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rotation = await dbContext.Set<CrewRotation>()
            .SingleOrDefaultAsync(candidate => candidate.Id == rotationId, cancellationToken)
            ?? throw new CrewRotationNotFoundException(rotationId);

        var errors = CrewLifecycleValidator.ValidateNotes(request.Notes, CrewRotation.CancellationNotesMaxLength);
        if (errors.Count > 0)
        {
            throw new CrewLifecycleValidationException(errors);
        }

        rotation.Cancel(actingUserId, timeProvider.GetUtcNow().UtcDateTime, CrewLifecycleValidator.NormalizeNotes(request.Notes));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
