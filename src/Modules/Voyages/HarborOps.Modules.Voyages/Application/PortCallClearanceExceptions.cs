namespace HarborOps.Modules.Voyages.Application;

internal sealed class PortCallClearanceValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Port-call clearance validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PortCallClearanceConflictException(Guid voyageStopId, Exception innerException)
    : Exception(
        $"Voyage stop '{voyageStopId}' or its port-call clearance was changed by another request.",
        innerException)
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
