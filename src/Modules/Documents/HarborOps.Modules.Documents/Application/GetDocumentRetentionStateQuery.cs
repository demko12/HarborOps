using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentRetentionStateQuery(DbContext dbContext)
    : IGetDocumentRetentionStateQuery
{
    public async Task<string> GetStateAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var state = await dbContext.Set<DocumentRetentionState>()
            .AsNoTracking()
            .Where(candidate => candidate.DocumentId == documentId)
            .Select(candidate => candidate.State)
            .SingleOrDefaultAsync(cancellationToken);

        return state ?? DocumentRetentionState.ActiveState;
    }
}
