namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyTextRequiredException(
    Guid discrepancyId,
    string requestedAction,
    string requirement)
    : Exception($"Discrepancy '{discrepancyId}' cannot be {requestedAction} without {requirement}.")
{
    internal const string NoteRequirement = "a note";
    internal const string VoidReasonRequirement = "a reason";

    internal static string ResolutionSummaryRequirement =>
        $"a resolution summary of at least {CargoDiscrepancy.ResolutionSummaryMinLength} characters";

    internal Guid DiscrepancyId { get; } = discrepancyId;

    internal string RequestedAction { get; } = requestedAction;

    internal string Requirement { get; } = requirement;
}
