using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Documents.Application;

internal sealed class ReplaceDocumentFileService(
    DbContext dbContext,
    IDocumentFileStorage storage,
    IOptions<DocumentFileValidationOptions> validationOptions,
    IDateTimeProvider dateTimeProvider,
    ILogger<ReplaceDocumentFileService> logger)
    : IReplaceDocumentFileService
{
    public async Task<DocumentFileResponse> ReplaceAsync(
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

        if (!document.HasFile)
        {
            throw new DocumentFileNotFoundException(request.DocumentId);
        }

        var errors = UploadDocumentFileValidator.Validate(request, validationOptions.Value);
        if (errors.Count > 0)
        {
            throw new UploadDocumentFileValidationException(errors);
        }

        var oldStorageKey = document.StorageKey!;

        string newStorageKey;
        try
        {
            newStorageKey = await storage.SaveAsync(request.Content, cancellationToken);
        }
        catch (DocumentFileStorageException)
        {
            logger.LogWarning("Failed to store replacement file for document {DocumentId}.", request.DocumentId);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Failed to store replacement file for document {DocumentId}.", request.DocumentId);
            throw new DocumentFileStorageException("Failed to save the document file to storage.", exception);
        }

        document.AttachFile(
            request.FileName,
            request.ContentType,
            newStorageKey,
            request.SizeBytes,
            request.UploadedBy,
            dateTimeProvider.UtcNow);

        if (string.Equals(document.ReviewStatus, Document.RejectedReviewStatus, StringComparison.Ordinal))
        {
            document.ReturnToDraftAfterCorrection();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(newStorageKey, cancellationToken);
            }
            catch
            {
                // Best-effort cleanup of the orphaned replacement file; the save
                // failure caught above is the one that must reach the caller.
            }

            logger.LogWarning(
                "Failed to persist replacement file for document {DocumentId}; cleanup of the orphaned file was attempted.",
                request.DocumentId);

            throw;
        }

        try
        {
            await storage.DeleteAsync(oldStorageKey, cancellationToken);
        }
        catch
        {
            // Best-effort: the replacement already succeeded, so a failure to
            // remove the superseded file only leaves it orphaned in storage.
            logger.LogWarning(
                "Failed to delete superseded file for document {DocumentId} after a successful replace.",
                request.DocumentId);
        }

        logger.LogInformation(
            "Document file replaced for {DocumentId}, size {SizeBytes} bytes",
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
