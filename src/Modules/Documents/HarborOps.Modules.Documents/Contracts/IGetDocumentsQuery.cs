namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentsQuery
{
    Task<GetDocumentsResponse> GetDocumentsAsync(
        GetDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false);
}
