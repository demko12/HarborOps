namespace HarborOps.Api.Composition.Operations.Dashboard;

public sealed record OperationsDashboardResponse(
    DateOnly AsOf,
    IReadOnlyList<OperationsDashboardSectionResponse> Sections,
    IReadOnlyList<OperationsDashboardPriorityItemResponse> PriorityItems);

public sealed record OperationsDashboardSectionResponse(
    string Capability,
    string Title,
    string SummaryLabel,
    int Count,
    string Link,
    IReadOnlyList<OperationsDashboardAttentionItemResponse> Items,
    bool IsUnavailable);

public sealed record OperationsDashboardAttentionItemResponse(
    string Title,
    string Detail,
    string? DueLabel,
    string Severity);

public sealed record OperationsDashboardPriorityItemResponse(
    string Capability,
    string CapabilityTitle,
    string Title,
    string Detail,
    string? DueLabel,
    string Severity,
    string Link);
