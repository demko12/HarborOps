namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

public sealed class MaintenanceBacklogReportValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Maintenance backlog report validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
