namespace HarborOps.Modules.Voyages.Contracts;

public interface IOperationsDashboardVoyagesQuery
{
    Task<OperationsDashboardVoyagesResult> GetUpcomingVoyagesAsync(
        DateTime asOfUtc,
        DateTime throughUtc,
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardVoyagesResult(
    int UpcomingVoyageCount,
    IReadOnlyList<OperationsDashboardVoyageItem> Items);

public sealed record OperationsDashboardVoyageItem(
    Guid VoyageId,
    string VoyageNumber,
    string DeparturePortCode,
    string ArrivalPortCode,
    string Status,
    DateTime PlannedDeparture);
