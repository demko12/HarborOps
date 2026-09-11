namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceItemNotPendingException(Guid itemId, string currentStatus)
    : Exception(
        $"Port-call clearance item '{itemId}' is '{currentStatus}' and can no longer be changed. "
        + "Reopen the clearance and add a replacement item instead.")
{
    internal Guid ItemId { get; } = itemId;

    internal string CurrentStatus { get; } = currentStatus;
}
