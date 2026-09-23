namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementNotFoundException(Guid voyageStopId)
    : Exception($"Voyage stop '{voyageStopId}' has no port-call statement.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
