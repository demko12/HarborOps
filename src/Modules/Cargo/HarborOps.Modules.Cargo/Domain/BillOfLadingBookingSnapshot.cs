namespace HarborOps.Modules.Cargo.Domain;

internal sealed record BillOfLadingBookingSnapshot(
    string BookingNumber,
    Guid VoyageId,
    string CargoType,
    string Description,
    int PackageCount,
    decimal WeightKg,
    CargoStatus Status);
