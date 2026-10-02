namespace HarborOps.Modules.Documents.Contracts;

public sealed record ExpiringDocumentsMonthCount(
    int Year,
    int Month,
    int Count);
