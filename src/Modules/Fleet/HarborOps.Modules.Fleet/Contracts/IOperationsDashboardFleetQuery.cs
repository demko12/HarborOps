namespace HarborOps.Modules.Fleet.Contracts;

public interface IOperationsDashboardFleetQuery
{
    Task<OperationsDashboardFleetResult> GetActiveVesselsAsync(
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardFleetResult(
    int ActiveVesselCount,
    IReadOnlyList<OperationsDashboardFleetItem> Items);

public sealed record OperationsDashboardFleetItem(
    Guid VesselId,
    string Name,
    string CurrentStatus);
