namespace HarborOps.Modules.Cargo.Contracts;

public sealed record SearchCargoBookingsResponse(
    IReadOnlyList<CargoBookingListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int HazardousCount,
    int OpenDiscrepancyBookingCount);
