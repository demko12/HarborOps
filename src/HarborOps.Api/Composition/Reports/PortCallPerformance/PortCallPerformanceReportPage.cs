namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportPage(
    DateOnly DepartedFrom,
    DateOnly DepartedTo,
    IReadOnlyList<PortCallPerformanceReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    PortCallPerformanceReportSummary Summary,
    IReadOnlyList<PortCallPerformanceReportPortSummary> Ports);
