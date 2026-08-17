using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class OperationsDashboardDocumentsQuery(DbContext dbContext)
    : IOperationsDashboardDocumentsQuery
{
    public async Task<OperationsDashboardDocumentsResult> GetExpiringDocumentsAsync(
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var documents = dbContext.Set<Document>()
            .AsNoTracking()
            .Where(document => document.ExpiryDate <= through);

        var expiringDocumentCount = await documents.CountAsync(cancellationToken);
        var items = await documents
            .OrderBy(document => document.ExpiryDate)
            .ThenBy(document => document.Id)
            .Take(maximumItems)
            .Select(document => new OperationsDashboardDocumentItem(
                document.Id,
                document.DocumentType.Name,
                document.Number,
                document.ExpiryDate,
                document.Status))
            .ToListAsync(cancellationToken);

        return new OperationsDashboardDocumentsResult(expiringDocumentCount, items);
    }
}
