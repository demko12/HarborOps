namespace HarborOps.Modules.Documents.Contracts;

public interface IGetDocumentReadinessQuery
{
    Task<GetDocumentReadinessResponse> GetAsync(
        GetDocumentReadinessRequest request,
        bool restrictToReadOnlyVisible,
        CancellationToken cancellationToken);
}
