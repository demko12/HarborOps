namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceNotCompletedException(
    Guid portCallClearanceId,
    string currentStatus)
    : Exception(
        $"Port-call clearance '{portCallClearanceId}' cannot be reopened while its status is '{currentStatus}'.")
{
    internal Guid PortCallClearanceId { get; } = portCallClearanceId;

    internal string CurrentStatus { get; } = currentStatus;
}
