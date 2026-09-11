namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceAlreadyCompletedException(Guid portCallClearanceId)
    : Exception($"Port-call clearance '{portCallClearanceId}' is already completed.")
{
    internal Guid PortCallClearanceId { get; } = portCallClearanceId;
}
