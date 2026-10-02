namespace HarborOps.Modules.Documents.Contracts;

public interface IGetExpiringDocumentsQuery
{
    Task<GetExpiringDocumentsResponse> GetExpiringDocumentsAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false);

    Task<GetExpiringDocumentsSummaryResponse> GetSummaryAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false);
}
