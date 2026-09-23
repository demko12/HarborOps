namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityLimitReachedException(Guid voyageStopId, int limit)
    : Exception($"Voyage stop '{voyageStopId}' already has the maximum of {limit} port-call activities.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal int Limit { get; } = limit;
}
