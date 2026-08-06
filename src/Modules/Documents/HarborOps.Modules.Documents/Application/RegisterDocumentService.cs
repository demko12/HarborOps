using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class RegisterDocumentService(DbContext dbContext)
{
    public async Task<RegisterDocumentResponse> RegisterAsync(
        RegisterDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterDocumentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterDocumentValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var documentTypeId = request.DocumentTypeId!.Value;
        var number = request.Number!.Trim();
        var issueDate = request.IssueDate!.Value;
        var expiryDate = request.ExpiryDate!.Value;
        var issuingAuthority = request.IssuingAuthority!.Trim();
        var status = request.Status!.Trim();

        var documentTypeExists = await dbContext.Set<DocumentType>()
            .AsNoTracking()
            .AnyAsync(documentType => documentType.Id == documentTypeId, cancellationToken);

        if (!documentTypeExists)
        {
            throw new DocumentTypeNotFoundException(documentTypeId);
        }

        var documents = dbContext.Set<Document>();

        var numberExists = await documents
            .AsNoTracking()
            .AnyAsync(
                document => document.VesselId == vesselId && document.Number == number,
                cancellationToken);

        if (numberExists)
        {
            throw new DuplicateDocumentNumberException(number, vesselId);
        }

        var document = new Document(
            vesselId,
            documentTypeId,
            number,
            issueDate,
            expiryDate,
            issuingAuthority,
            status);

        await documents.AddAsync(document, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterDocumentResponse(document.Id);
    }
}
