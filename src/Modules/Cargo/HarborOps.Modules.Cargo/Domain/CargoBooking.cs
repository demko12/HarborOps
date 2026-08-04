namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoBooking
{
    internal const int BookingNumberMaxLength = 50;
    internal const int CargoTypeMaxLength = 100;
    internal const int DescriptionMaxLength = 500;
    internal const int StatusMaxLength = 50;
    internal const int WeightKgPrecision = 18;
    internal const int WeightKgScale = 3;

    private CargoBooking()
    {
    }

    private CargoBooking(
        Guid id,
        string bookingNumber,
        Guid voyageId,
        string description,
        string cargoType,
        decimal weightKg,
        int packageCount,
        bool isHazardous,
        CargoStatus status,
        DateTime createdUtc)
    {
        Id = id;
        BookingNumber = bookingNumber;
        VoyageId = voyageId;
        Description = description;
        CargoType = cargoType;
        WeightKg = weightKg;
        PackageCount = packageCount;
        IsHazardous = isHazardous;
        Status = status;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }

    public string BookingNumber { get; private set; } = null!;

    public Guid VoyageId { get; private set; }

    public string Description { get; private set; } = null!;

    public string CargoType { get; private set; } = null!;

    public decimal WeightKg { get; private set; }

    public int PackageCount { get; private set; }

    public bool IsHazardous { get; private set; }

    public CargoStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    internal static CargoBooking Register(
        string bookingNumber,
        Guid voyageId,
        string description,
        string cargoType,
        decimal weightKg,
        int packageCount,
        bool isHazardous)
    {
        return new CargoBooking(
            Guid.NewGuid(),
            bookingNumber,
            voyageId,
            description,
            cargoType,
            weightKg,
            packageCount,
            isHazardous,
            CargoStatus.Registered,
            DateTime.UtcNow);
    }
}
