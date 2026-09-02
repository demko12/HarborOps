namespace HarborOps.Modules.Voyages.Domain;

internal sealed class BerthScheduleNotReservedException(Guid berthScheduleId, string currentStatus)
    : Exception(
        $"Berth schedule '{berthScheduleId}' cannot be occupied while its status is '{currentStatus}'.")
{
    internal Guid BerthScheduleId { get; } = berthScheduleId;

    internal string CurrentStatus { get; } = currentStatus;
}
