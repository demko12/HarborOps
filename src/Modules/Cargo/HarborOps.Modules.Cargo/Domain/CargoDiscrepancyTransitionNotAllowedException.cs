namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyTransitionNotAllowedException(
    Guid discrepancyId,
    CargoDiscrepancyStatus currentStatus,
    string requestedAction,
    string reason)
    : Exception(
        $"Discrepancy '{discrepancyId}' cannot be {requestedAction} while it is '{currentStatus}' because {reason}.")
{
    internal const string StartInvestigationAction = "taken under investigation";
    internal const string AddNoteAction = "annotated";
    internal const string ResolveAction = "resolved";
    internal const string VoidAction = "voided";

    internal const string AlreadyInvestigatingReason = "it is already under investigation";
    internal const string NotInvestigatingReason = "it is not under investigation";
    internal const string AlreadyResolvedReason = "it is already resolved";
    internal const string AlreadyVoidedReason = "it is already voided";
    internal const string NoInvestigationNoteReason =
        "no investigation note has been recorded yet";

    internal Guid DiscrepancyId { get; } = discrepancyId;

    internal CargoDiscrepancyStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;

    internal string Reason { get; } = reason;
}
