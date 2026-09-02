namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageScheduleUnchangedException(Guid voyageId)
    : Exception($"Voyage '{voyageId}' already has the requested schedule.")
{
    internal Guid VoyageId { get; } = voyageId;
}
