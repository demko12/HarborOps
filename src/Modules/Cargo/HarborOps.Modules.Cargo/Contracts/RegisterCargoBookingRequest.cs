namespace HarborOps.Modules.Cargo.Contracts;

public sealed record RegisterCargoBookingRequest
{
    public string? BookingNumber { get; init; }

    public Guid? VoyageId { get; init; }

    public string? Description { get; init; }

    public string? CargoType { get; init; }

    public decimal? WeightKg { get; init; }

    public int? PackageCount { get; init; }

    public bool? IsHazardous { get; init; }
}
