namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoBookingDetailsResponse(
    Guid CargoBookingId,
    string BookingNumber,
    Guid VoyageId,
    string Description,
    string CargoType,
    decimal WeightKg,
    int PackageCount,
    bool IsHazardous,
    string Status,
    string? NextStatus,
    DateTime CreatedUtc,
    CargoSafetyInfoDetails? SafetyInfo,
    IReadOnlyList<CargoTrackingEventListItem> TrackingEvents,
    IReadOnlyList<ContainerControlDetails> ContainerControls);
