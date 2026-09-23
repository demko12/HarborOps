namespace HarborOps.Modules.Voyages.Application;

internal sealed class PortCallLogValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Port-call activity log validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PortCallLogConflictException : Exception
{
    internal PortCallLogConflictException(Guid voyageStopId)
        : base($"The activity log or statement of voyage stop '{voyageStopId}' was changed by another request.")
    {
        VoyageStopId = voyageStopId;
    }

    internal PortCallLogConflictException(Guid voyageStopId, Exception innerException)
        : base(
            $"The activity log or statement of voyage stop '{voyageStopId}' was changed by another request.",
            innerException)
    {
        VoyageStopId = voyageStopId;
    }

    internal Guid VoyageStopId { get; }
}

internal sealed class PortCallStatementSourceChangedException(Guid voyageStopId)
    : Exception(
        $"The stop times, labels, activities or delays of voyage stop '{voyageStopId}' changed after the finalization preview.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
