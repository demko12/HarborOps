namespace HarborOps.Modules.Compliance.Domain;

internal sealed class InvalidNonConformityTransitionException(
    int nonConformityId,
    string currentStatus,
    string requestedStatus)
    : Exception(
        $"Non-conformity '{nonConformityId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal int NonConformityId { get; } = nonConformityId;

    internal string CurrentStatus { get; } = currentStatus;

    internal string RequestedStatus { get; } = requestedStatus;
}
