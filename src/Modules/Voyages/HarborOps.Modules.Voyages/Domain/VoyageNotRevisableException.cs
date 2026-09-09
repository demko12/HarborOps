namespace HarborOps.Modules.Voyages.Domain;

public sealed class VoyageNotRevisableException(Guid voyageId, string currentStatus)
    : Exception(
        $"Voyage '{voyageId}' cannot receive revised estimates while its status is "
        + $"'{currentStatus}'.")
{
    public Guid VoyageId { get; } = voyageId;

    public string CurrentStatus { get; } = currentStatus;
}
