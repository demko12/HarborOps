namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

public sealed class ComplianceExposureReportValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Compliance exposure report validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
