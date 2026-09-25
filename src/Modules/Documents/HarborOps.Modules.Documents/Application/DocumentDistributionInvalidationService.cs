using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

/// <summary>Keeps pending acknowledgements from surviving a changed document snapshot.</summary>
public sealed class DocumentDistributionInvalidationService(DbContext dbContext, TimeProvider timeProvider)
{
    internal async Task ObsoletePendingAsync(Guid documentId, string reason, CancellationToken cancellationToken)
    {
        var issueIds = await dbContext.Set<DocumentDistributionIssue>().Where(x => x.DocumentId == documentId && x.CancelledAtUtc == null).Select(x => x.Id).ToListAsync(cancellationToken);
        if (issueIds.Count == 0) return;
        var pending = await dbContext.Set<DocumentDistributionRecipient>().Where(x => issueIds.Contains(x.IssueId) && x.State == DocumentDistributionRecipient.PendingState).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var recipient in pending) recipient.MarkObsolete(now, reason);
    }
}
