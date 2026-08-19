using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentFileQuery(
    DbContext dbContext,
    IDocumentFileStorage storage,
    ILogger<GetDocumentFileQuery> logger)
    : IGetDocumentFileQuery
{
    public async Task<DocumentFileContent> GetFileAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var fileInfo = await dbContext.Set<Document>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == documentId)
            .Select(candidate => new
            {
                candidate.FileName,
                candidate.ContentType,
                candidate.SizeBytes,
                candidate.StorageKey,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (fileInfo is null)
        {
            logger.LogDebug("Document {DocumentId} was not found.", documentId);
            throw new DocumentNotFoundException(documentId);
        }

        if (fileInfo.StorageKey is null)
        {
            logger.LogDebug("Document {DocumentId} does not have an attached file.", documentId);
            throw new DocumentFileNotFoundException(documentId);
        }

        Stream stream;
        try
        {
            stream = await storage.OpenReadAsync(fileInfo.StorageKey, cancellationToken);
        }
        catch (DocumentFileStorageException)
        {
            logger.LogWarning("Failed to retrieve file for document {DocumentId}.", documentId);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Failed to retrieve file for document {DocumentId}.", documentId);
            throw new DocumentFileStorageException("Failed to open the document file from storage.", exception);
        }

        logger.LogInformation("Document file retrieved for {DocumentId}", documentId);

        return new DocumentFileContent(
            stream,
            fileInfo.FileName!,
            fileInfo.ContentType!,
            fileInfo.SizeBytes!.Value);
    }
}
