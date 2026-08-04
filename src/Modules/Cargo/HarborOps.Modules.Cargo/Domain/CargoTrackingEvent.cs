namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoTrackingEvent
{
    internal const int LocationMaxLength = 200;
    internal const int StatusMaxLength = 50;

    private CargoTrackingEvent()
    {
    }

    private CargoTrackingEvent(
        Guid id,
        Guid cargoBookingId,
        Guid responsibleUserId,
        CargoStatus status,
        DateTime occurredUtc,
        string location)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        ResponsibleUserId = responsibleUserId;
        Status = status;
        OccurredUtc = occurredUtc;
        Location = location;
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public Guid ResponsibleUserId { get; private set; }

    public CargoStatus Status { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    public string Location { get; private set; } = null!;

    internal static CargoTrackingEvent Record(
        Guid cargoBookingId,
        Guid responsibleUserId,
        CargoStatus status,
        DateTime occurredUtc,
        string location)
    {
        return new CargoTrackingEvent(
            Guid.NewGuid(),
            cargoBookingId,
            responsibleUserId,
            status,
            occurredUtc,
            location);
    }
}
