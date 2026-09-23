namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementAlreadyExistsException(Guid voyageStopId)
    : Exception($"Voyage stop '{voyageStopId}' already has a port-call statement.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
