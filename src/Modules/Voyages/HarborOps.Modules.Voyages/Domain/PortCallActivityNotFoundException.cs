namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityNotFoundException(Guid portCallActivityId)
    : Exception($"Port-call activity '{portCallActivityId}' was not found for this voyage stop.")
{
    internal Guid PortCallActivityId { get; } = portCallActivityId;
}
