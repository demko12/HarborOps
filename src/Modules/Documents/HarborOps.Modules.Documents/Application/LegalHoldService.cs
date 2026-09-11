using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class LegalHoldService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<PlaceLegalHoldResponse> PlaceAsync(
        Guid documentId,
        PlaceLegalHoldRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var documentExists = await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            throw new DocumentNotFoundException(documentId);
        }

        var hold = LegalHold.Place(documentId, request.Reason!, request.Reference, actorId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.Set<LegalHold>().AddAsync(hold, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PlaceLegalHoldResponse(hold.Id);
    }

    public async Task<ReleaseLegalHoldResponse> ReleaseAsync(
        Guid documentId,
        Guid holdId,
        ReleaseLegalHoldRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hold = await dbContext.Set<LegalHold>().SingleOrDefaultAsync(h => h.Id == holdId, cancellationToken);

        if (hold is null || hold.DocumentId != documentId)
        {
            throw new LegalHoldNotFoundException(holdId);
        }

        hold.Release(actorId, request.Reason!, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReleaseLegalHoldResponse(hold.Id, hold.IsActive);
    }
}
