namespace HarborOps.Modules.Voyages.Domain;

internal sealed class BerthScheduleAlreadyReleasedException(Guid berthScheduleId)
    : Exception($"Berth schedule '{berthScheduleId}' has already been released.")
{
    internal Guid BerthScheduleId { get; } = berthScheduleId;
}
