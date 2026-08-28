namespace HarborOps.Modules.Voyages.Application;

internal sealed class VoyageStopNotFoundException(Guid voyageStopId)
    : Exception($"Voyage stop '{voyageStopId}' was not found.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
