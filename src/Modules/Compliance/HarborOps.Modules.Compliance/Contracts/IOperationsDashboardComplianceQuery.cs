namespace HarborOps.Modules.Compliance.Contracts;

public interface IOperationsDashboardComplianceQuery
{
    Task<OperationsDashboardComplianceResult> GetAttentionItemsAsync(
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardComplianceResult(
    int AttentionItemCount,
    IReadOnlyList<OperationsDashboardComplianceItem> Items);

public sealed record OperationsDashboardComplianceItem(
    string ItemType,
    string Title,
    DateOnly DueDate,
    string Severity,
    string SortKey);
