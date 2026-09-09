namespace HarborOps.Modules.Voyages.Domain;

public sealed class VoyageStopNotRevisableException(Guid voyageStopId, string currentStatus)
    : Exception(
        $"Voyage stop '{voyageStopId}' cannot receive a revised estimate while its status is "
        + $"'{currentStatus}'.")
{
    public Guid VoyageStopId { get; } = voyageStopId;

    public string CurrentStatus { get; } = currentStatus;
}
