namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportMetric(
    int? Minutes,
    decimal? Hours,
    string Quality,
    string? Milestone);
