using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentByIdQuery(DbContext dbContext, TimeProvider? timeProvider = null)
    : IGetDocumentByIdQuery
{
    public async Task<DocumentDetailsResponse> GetByIdAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var today = timeProvider is null ? DateOnly.MinValue : DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var document = await dbContext.Set<Document>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == documentId)
            .Select(candidate => new DocumentDetailsResponse(
                candidate.Id,
                candidate.VesselId,
                candidate.DocumentTypeId,
                candidate.DocumentType.Name,
                candidate.Number,
                candidate.IssueDate,
                candidate.ExpiryDate,
                candidate.IssuingAuthority,
                candidate.Status == Document.ActiveStatus && candidate.ExpiryDate < today ? "Expired" : candidate.Status,
                candidate.ReviewStatus,
                candidate.StorageKey != null,
                candidate.FileName,
                candidate.ContentType,
                candidate.SizeBytes,
                candidate.UploadedBy,
                candidate.UploadedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(documentId);
        }

        return document;
    }
}
