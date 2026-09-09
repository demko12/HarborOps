namespace HarborOps.Modules.Voyages.Application;

public sealed class VoyageStopNotFoundException(Guid voyageStopId)
    : Exception($"Voyage stop '{voyageStopId}' was not found.")
{
    public Guid VoyageStopId { get; } = voyageStopId;
}
