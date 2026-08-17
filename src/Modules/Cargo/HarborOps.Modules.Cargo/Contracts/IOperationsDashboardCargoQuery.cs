namespace HarborOps.Modules.Cargo.Contracts;

public interface IOperationsDashboardCargoQuery
{
    Task<OperationsDashboardCargoResult> GetOpenHazardousBookingsAsync(
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardCargoResult(
    int OpenHazardousBookingCount,
    IReadOnlyList<OperationsDashboardCargoItem> Items);

public sealed record OperationsDashboardCargoItem(
    Guid CargoBookingId,
    string BookingNumber,
    string CargoType,
    decimal WeightKg,
    string Status);
