using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class DocumentRetentionService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<DocumentRetentionStateResponse> ArchiveAsync(Guid documentId, Guid actorId, CancellationToken cancellationToken)
    {
        var state = await LoadOrCreateStateAsync(documentId, cancellationToken);

        state.Archive(actorId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentRetentionStateResponse(state.DocumentId, state.State);
    }

    public async Task<DocumentRetentionStateResponse> RestoreAsync(Guid documentId, Guid actorId, CancellationToken cancellationToken)
    {
        var state = await LoadOrCreateStateAsync(documentId, cancellationToken);

        state.Restore(actorId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentRetentionStateResponse(state.DocumentId, state.State);
    }

    private async Task<DocumentRetentionState> LoadOrCreateStateAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var documentExists = await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            throw new DocumentNotFoundException(documentId);
        }

        var state = await dbContext.Set<DocumentRetentionState>().SingleOrDefaultAsync(s => s.DocumentId == documentId, cancellationToken);

        if (state is null)
        {
            state = new DocumentRetentionState(documentId);
            await dbContext.Set<DocumentRetentionState>().AddAsync(state, cancellationToken);
        }

        return state;
    }
}
