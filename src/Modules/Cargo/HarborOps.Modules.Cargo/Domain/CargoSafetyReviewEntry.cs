namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoSafetyReviewEntry
{
    internal const int StatusMaxLength = 50;

    private CargoSafetyReviewEntry()
    {
    }

    private CargoSafetyReviewEntry(
        Guid id,
        Guid cargoSafetyInfoId,
        CargoSafetyReviewStatus fromStatus,
        CargoSafetyReviewStatus toStatus,
        Guid actorUserId,
        DateTime occurredUtc,
        string? notes)
    {
        Id = id;
        CargoSafetyInfoId = cargoSafetyInfoId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = actorUserId;
        OccurredUtc = occurredUtc;
        Notes = notes;
    }

    public Guid Id { get; private set; }

    public Guid CargoSafetyInfoId { get; private set; }

    public CargoSafetyReviewStatus FromStatus { get; private set; }

    public CargoSafetyReviewStatus ToStatus { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    public string? Notes { get; private set; }

    internal static CargoSafetyReviewEntry Record(
        Guid cargoSafetyInfoId,
        CargoSafetyReviewStatus fromStatus,
        CargoSafetyReviewStatus toStatus,
        Guid actorUserId,
        DateTime occurredUtc,
        string? notes)
    {
        return new CargoSafetyReviewEntry(
            Guid.NewGuid(),
            cargoSafetyInfoId,
            fromStatus,
            toStatus,
            actorUserId,
            occurredUtc,
            notes);
    }
}
