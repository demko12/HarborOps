namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

public sealed record ComplianceExposureReportPage(
    DateOnly AsOf,
    IReadOnlyList<ComplianceExposureReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
