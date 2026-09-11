namespace HarborOps.Modules.Cargo.Domain;

internal sealed class BillOfLadingTransitionNotAllowedException(
    Guid billOfLadingId,
    BillOfLadingStatus currentStatus,
    string requestedAction,
    string reason)
    : Exception(
        $"Bill of lading '{billOfLadingId}' cannot be {requestedAction} while it is '{currentStatus}' because {reason}.")
{
    internal const string UpdateAction = "edited";
    internal const string IssueAction = "issued";
    internal const string VoidAction = "voided";

    internal const string AlreadyIssuedReason = "it is already issued";
    internal const string AlreadyVoidedReason = "it is already voided";
    internal const string StillDraftReason = "it has not been issued yet";
    internal const string NoContainersReason = "no container is selected";
    internal const string BookingNotLoadedReason =
        "its booking has not reached the Loaded status";
    internal const string IncompleteReason =
        "its party, routing or cargo details are incomplete";

    internal Guid BillOfLadingId { get; } = billOfLadingId;

    internal BillOfLadingStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;

    internal string Reason { get; } = reason;
}
