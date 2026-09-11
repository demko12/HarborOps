namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStopAlreadyDepartedException(Guid voyageStopId)
    : Exception(
        $"Voyage stop '{voyageStopId}' has already departed and its port-call clearance can no longer be changed.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;
}
