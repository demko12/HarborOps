namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentFileQuery
{
    Task<DocumentFileContent> GetFileAsync(Guid documentId, CancellationToken cancellationToken);
}
