namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStopNotPlannedException(Guid voyageStopId, string currentStatus)
    : Exception($"Voyage stop '{voyageStopId}' cannot be changed while its status is '{currentStatus}'.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal string CurrentStatus { get; } = currentStatus;
}
