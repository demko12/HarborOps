namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageNotExecutingException(Guid voyageId, string currentStatus)
    : Exception(
        $"Voyage '{voyageId}' cannot execute stop actions while its status is '{currentStatus}'.")
{
    internal Guid VoyageId { get; } = voyageId;

    internal string CurrentStatus { get; } = currentStatus;
}
