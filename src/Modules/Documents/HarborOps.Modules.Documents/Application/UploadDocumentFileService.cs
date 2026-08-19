using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Documents.Application;

internal sealed class UploadDocumentFileService(
    DbContext dbContext,
    IDocumentFileStorage storage,
    IOptions<DocumentFileValidationOptions> validationOptions,
    IDateTimeProvider dateTimeProvider,
    ILogger<UploadDocumentFileService> logger)
    : IUploadDocumentFileService
{
    public async Task<DocumentFileResponse> UploadAsync(
        UploadDocumentFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await dbContext.Set<Document>()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.DocumentId, cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(request.DocumentId);
        }

        if (document.HasFile)
        {
            throw new DocumentAlreadyHasFileException(request.DocumentId);
        }

        var errors = UploadDocumentFileValidator.Validate(request, validationOptions.Value);
        if (errors.Count > 0)
        {
            throw new UploadDocumentFileValidationException(errors);
        }

        string storageKey;
        try
        {
            storageKey = await storage.SaveAsync(request.Content, cancellationToken);
        }
        catch (DocumentFileStorageException)
        {
            logger.LogWarning("Failed to store file for document {DocumentId}.", request.DocumentId);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Failed to store file for document {DocumentId}.", request.DocumentId);
            throw new DocumentFileStorageException("Failed to save the document file to storage.", exception);
        }

        document.AttachFile(
            request.FileName,
            request.ContentType,
            storageKey,
            request.SizeBytes,
            request.UploadedBy,
            dateTimeProvider.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Document file uploaded for {DocumentId}, size {SizeBytes} bytes",
            request.DocumentId,
            request.SizeBytes);

        return new DocumentFileResponse(
            document.FileName!,
            document.ContentType!,
            document.SizeBytes!.Value,
            document.UploadedBy!.Value,
            document.UploadedAtUtc!.Value);
    }
}
