namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportPortSummary(
    Guid PortId,
    string PortName,
    string? PortUnLocode,
    PortCallPerformanceReportSummary Summary);
