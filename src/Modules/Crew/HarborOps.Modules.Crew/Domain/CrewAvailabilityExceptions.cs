namespace HarborOps.Modules.Crew.Domain;

internal sealed class InvalidCrewAvailabilityTransitionException(
    Guid requestId,
    string currentStatus,
    string attemptedAction)
    : Exception($"Crew availability request '{requestId}' cannot {attemptedAction} from status '{currentStatus}'.")
{
    internal Guid RequestId { get; } = requestId;

    internal string CurrentStatus { get; } = currentStatus;

    internal string AttemptedAction { get; } = attemptedAction;
}
