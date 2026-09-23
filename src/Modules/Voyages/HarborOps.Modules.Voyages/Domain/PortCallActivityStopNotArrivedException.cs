namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityStopNotArrivedException(Guid voyageStopId, string currentStatus)
    : Exception(
        $"Voyage stop '{voyageStopId}' has no recorded arrival, so port-call activities cannot be recorded while its status is '{currentStatus}'.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal string CurrentStatus { get; } = currentStatus;
}
