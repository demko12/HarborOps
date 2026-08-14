namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetVesselComplianceOverviewValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get vessel compliance overview validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
