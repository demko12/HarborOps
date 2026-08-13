namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoBookingListItem(
    Guid CargoBookingId,
    string BookingNumber,
    Guid VoyageId,
    string Description,
    string CargoType,
    decimal WeightKg,
    int PackageCount,
    bool IsHazardous,
    string Status,
    DateTime CreatedUtc);
