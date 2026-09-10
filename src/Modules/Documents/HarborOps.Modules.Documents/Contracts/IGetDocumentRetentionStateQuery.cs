namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentRetentionStateQuery
{
    Task<string> GetStateAsync(Guid documentId, CancellationToken cancellationToken);
}
