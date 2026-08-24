namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class DryDockTransitionNotAllowedException(
    Guid dryDockId,
    DryDockStatus currentStatus,
    DryDockStatus requestedStatus)
    : Exception(
        $"Dry dock '{dryDockId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid DryDockId { get; } = dryDockId;

    internal DryDockStatus CurrentStatus { get; } = currentStatus;

    internal DryDockStatus RequestedStatus { get; } = requestedStatus;
}
