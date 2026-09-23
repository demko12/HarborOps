namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityVoidedException(Guid portCallActivityId)
    : Exception($"Port-call activity '{portCallActivityId}' is voided and can no longer be changed.")
{
    internal Guid PortCallActivityId { get; } = portCallActivityId;
}
