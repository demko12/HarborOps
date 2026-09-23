namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementNotFinalizableException(
    Guid voyageStopId,
    IReadOnlyList<PortCallStatementBlocker> blockers)
    : Exception(
        $"The port-call statement for voyage stop '{voyageStopId}' cannot be finalized: {string.Join(" ", blockers.Select(blocker => blocker.Message))}")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal IReadOnlyList<PortCallStatementBlocker> Blockers { get; } = blockers;
}
