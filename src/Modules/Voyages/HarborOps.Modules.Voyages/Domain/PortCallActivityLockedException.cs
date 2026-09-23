namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityLockedException(Guid portCallActivityId)
    : Exception(
        $"Port-call activity '{portCallActivityId}' belongs to a finalized statement and can no longer be changed.")
{
    internal Guid PortCallActivityId { get; } = portCallActivityId;
}
