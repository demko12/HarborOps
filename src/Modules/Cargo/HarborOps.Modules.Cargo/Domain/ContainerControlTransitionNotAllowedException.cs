namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ContainerControlTransitionNotAllowedException(
    Guid containerControlId,
    ContainerControlState currentState,
    string requestedAction,
    string reason)
    : Exception(
        $"Container control '{containerControlId}' cannot be {requestedAction} while it is '{currentState}' because {reason}.")
{
    internal const string AssignSealAction = "sealed";
    internal const string BreakSealAction = "sealed as broken";
    internal const string HoldAction = "held";
    internal const string ReleaseAction = "released";

    internal const string AlreadyHeldReason = "the container is already held";
    internal const string AlreadyReleasedReason = "the container is already released";
    internal const string HeldReason = "the container is held";
    internal const string InspectionMissingReason =
        "it has not been inspected yet";
    internal const string InspectionNotPassedReason =
        "its latest inspection did not pass";
    internal const string OpenTemperatureExcursionReason =
        "it has an unresolved temperature excursion";
    internal const string SealAlreadyAssignedReason =
        "that seal number is already assigned to it";
    internal const string SealAlreadyBrokenReason = "its seal is already broken";
    internal const string SealNotIntactReason = "it has no intact seal";

    internal Guid ContainerControlId { get; } = containerControlId;

    internal ContainerControlState CurrentState { get; } = currentState;

    internal string RequestedAction { get; } = requestedAction;

    internal string Reason { get; } = reason;
}
