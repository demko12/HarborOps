namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportSummary(
    int StopCount,
    int FinalizedCount,
    int NotFinalizedCount,
    PortCallPerformanceReportMetricSummary Turnaround,
    PortCallPerformanceReportMetricSummary ArrivalToAllFast,
    PortCallPerformanceReportMetricSummary CargoElapsed,
    PortCallPerformanceReportMetricSummary CargoCompletedToDeparture,
    int RecordedDelayCount,
    int RecordedDelayMinutes,
    decimal RecordedDelayHours);
