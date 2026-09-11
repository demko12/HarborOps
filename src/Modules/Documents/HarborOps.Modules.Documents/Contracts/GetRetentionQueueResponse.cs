namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetRetentionQueueResponse(
    IReadOnlyList<RetentionQueueItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
