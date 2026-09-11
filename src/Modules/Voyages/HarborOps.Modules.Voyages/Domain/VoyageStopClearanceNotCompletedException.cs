namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStopClearanceNotCompletedException(
    Guid voyageStopId,
    string clearanceStatus,
    int requiredItemCount,
    int unresolvedRequiredItemCount)
    : Exception(requiredItemCount == 0
        ? $"Voyage stop '{voyageStopId}' cannot depart because its port-call clearance is '{clearanceStatus}' and has no required items."
        : $"Voyage stop '{voyageStopId}' cannot depart while its port-call clearance is '{clearanceStatus}' with {unresolvedRequiredItemCount} unresolved required item(s).")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal string ClearanceStatus { get; } = clearanceStatus;

    internal int RequiredItemCount { get; } = requiredItemCount;

    internal int UnresolvedRequiredItemCount { get; } = unresolvedRequiredItemCount;
}
