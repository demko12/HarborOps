namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentTypesQuery
{
    Task<GetDocumentTypesResponse> GetDocumentTypesAsync(
        GetDocumentTypesRequest request,
        CancellationToken cancellationToken);
}
