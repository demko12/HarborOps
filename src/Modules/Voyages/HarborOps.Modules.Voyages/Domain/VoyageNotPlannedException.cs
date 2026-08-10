namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageNotPlannedException(Guid voyageId, string currentStatus)
    : Exception($"Voyage '{voyageId}' cannot depart while its status is '{currentStatus}'.")
{
    internal Guid VoyageId { get; } = voyageId;

    internal string CurrentStatus { get; } = currentStatus;
}
