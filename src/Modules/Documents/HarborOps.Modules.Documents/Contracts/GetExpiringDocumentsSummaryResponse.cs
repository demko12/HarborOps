namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetExpiringDocumentsSummaryResponse(
    int TotalCount,
    int ExpiredCount,
    int DueWithin30DaysCount,
    int PendingReviewCount,
    IReadOnlyList<ExpiringDocumentsMonthCount> Months);
