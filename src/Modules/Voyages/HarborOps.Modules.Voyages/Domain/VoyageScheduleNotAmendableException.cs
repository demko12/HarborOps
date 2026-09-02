namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageScheduleNotAmendableException(Guid voyageId, string currentStatus)
    : Exception(
        $"Voyage '{voyageId}' cannot change its schedule while its status is '{currentStatus}'.")
{
    internal Guid VoyageId { get; } = voyageId;

    internal string CurrentStatus { get; } = currentStatus;
}
