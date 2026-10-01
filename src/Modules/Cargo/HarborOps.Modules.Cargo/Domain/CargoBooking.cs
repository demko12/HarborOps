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
        ConcurrencyToken = Guid.NewGuid();
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

    public Guid ConcurrencyToken { get; private set; }

    internal CargoStatus? NextStatus => NextStatusFor(Status);

    internal bool AcceptsDiscrepancies => AcceptsDiscrepanciesAt(Status);

    internal static bool AcceptsDiscrepanciesAt(CargoStatus status)
    {
        return status is CargoStatus.Loaded
            or CargoStatus.InTransit
            or CargoStatus.Arrived
            or CargoStatus.Unloaded;
    }

    internal static CargoStatus? NextStatusFor(CargoStatus status)
    {
        return status == CargoStatus.Completed ? null : status + 1;
    }

    internal void AdvanceStatusTo(CargoStatus requestedStatus)
    {
        if (requestedStatus != NextStatus)
        {
            throw new CargoStatusTransitionNotAllowedException(Id, Status, requestedStatus);
        }

        Status = requestedStatus;
        Touch();
    }

    internal void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }

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
