namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageNotInProgressException(Guid voyageId, string currentStatus)
    : Exception($"Voyage '{voyageId}' cannot arrive while its status is '{currentStatus}'.")
{
    internal Guid VoyageId { get; } = voyageId;

    internal string CurrentStatus { get; } = currentStatus;
}
