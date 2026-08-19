namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentByIdQuery
{
    Task<DocumentDetailsResponse> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
}
