namespace HarborOps.Modules.Documents.Contracts;

public interface IOperationsDashboardDocumentsQuery
{
    Task<OperationsDashboardDocumentsResult> GetExpiringDocumentsAsync(
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardDocumentsResult(
    int ExpiringDocumentCount,
    IReadOnlyList<OperationsDashboardDocumentItem> Items);

public sealed record OperationsDashboardDocumentItem(
    Guid DocumentId,
    string DocumentTypeName,
    string Number,
    DateOnly ExpiryDate,
    string Status);
