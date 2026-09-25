namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed class PortCallPerformanceReportValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Port-call performance report validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
