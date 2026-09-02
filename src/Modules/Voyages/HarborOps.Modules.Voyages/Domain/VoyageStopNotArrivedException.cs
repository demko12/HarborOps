namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStopNotArrivedException(Guid voyageStopId, string currentStatus)
    : Exception($"Voyage stop '{voyageStopId}' cannot depart while its status is '{currentStatus}'.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal string CurrentStatus { get; } = currentStatus;
}
