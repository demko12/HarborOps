namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceNotFoundException(Guid voyageStopId)
    : Exception($"Voyage stop '{voyageStopId}' has no port-call clearance.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
