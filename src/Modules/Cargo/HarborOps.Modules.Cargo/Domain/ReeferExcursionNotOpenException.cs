namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferExcursionNotOpenException(
    Guid excursionId,
    ReeferExcursionStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Temperature excursion '{excursionId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal const string ResolveAction = "resolved";
    internal const string SupersedeAction = "superseded";

    internal Guid ExcursionId { get; } = excursionId;

    internal ReeferExcursionStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}
