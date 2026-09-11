namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceNotCompletableException(
    Guid portCallClearanceId,
    int requiredItemCount,
    int unresolvedRequiredItemCount)
    : Exception(requiredItemCount == 0
        ? $"Port-call clearance '{portCallClearanceId}' needs at least one required item before it can be completed."
        : $"Port-call clearance '{portCallClearanceId}' still has {unresolvedRequiredItemCount} required item(s) that are neither cleared nor waived.")
{
    internal Guid PortCallClearanceId { get; } = portCallClearanceId;

    internal int RequiredItemCount { get; } = requiredItemCount;

    internal int UnresolvedRequiredItemCount { get; } = unresolvedRequiredItemCount;
}
