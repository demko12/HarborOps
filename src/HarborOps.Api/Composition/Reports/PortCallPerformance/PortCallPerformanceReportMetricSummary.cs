namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportMetricSummary(
    decimal? MeanMinutes,
    decimal? MeanHours,
    int SampleCount,
    int MissingCount,
    int AmbiguousCount,
    int InvalidChronologyCount);
