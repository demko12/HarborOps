namespace HarborOps.Web.Api.Reports;

internal sealed record DocumentsExpiryReportQuery(
    DateOnly AsOf,
    Guid? VesselId,
    int? DocumentTypeId,
    DateOnly? From,
    DateOnly? To,
    string? Status,
    int PageNumber,
    int PageSize);

internal sealed record DocumentsExpiryReportRow(
    Guid DocumentId,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    int DaysUntilExpiry,
    bool IsExpired);

internal sealed record DocumentsExpiryReportPage(
    DateOnly AsOf,
    IReadOnlyList<DocumentsExpiryReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record ComplianceExposureReportQuery(
    DateOnly AsOf,
    Guid? VesselId,
    string? Category,
    int PageNumber,
    int PageSize);

internal sealed record ComplianceExposureReportRow(
    string ItemType,
    Guid VesselId,
    string Reference,
    string Description,
    DateOnly RelevantDate,
    string Status,
    string? Severity);

internal sealed record ComplianceExposureReportPage(
    DateOnly AsOf,
    IReadOnlyList<ComplianceExposureReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record MaintenanceBacklogReportQuery(
    DateOnly AsOf,
    Guid? VesselId,
    string? Priority,
    string? Status,
    bool? Overdue,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    int PageNumber,
    int PageSize);

internal sealed record MaintenanceBacklogReportRow(
    Guid WorkOrderId,
    Guid VesselId,
    string Title,
    string Priority,
    string Status,
    DateTime DueUtc,
    bool IsOverdue,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);

internal sealed record MaintenanceBacklogReportPage(
    DateOnly AsOf,
    IReadOnlyList<MaintenanceBacklogReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record ReportsApiResult<T>(T? Value, ReportsApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record ReportsApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;
}
