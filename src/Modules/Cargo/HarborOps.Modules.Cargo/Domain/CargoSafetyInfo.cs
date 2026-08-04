namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoSafetyInfo
{
    internal const int HandlingNotesMaxLength = 1000;
    internal const int ImdgClassMaxLength = 10;
    internal const int StorageConditionsMaxLength = 500;
    internal const int UnNumberMaxLength = 10;

    private CargoSafetyInfo()
    {
    }

    private CargoSafetyInfo(
        Guid id,
        Guid cargoBookingId,
        string imdgClass,
        string unNumber,
        string storageConditions,
        string? handlingNotes)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        ImdgClass = imdgClass;
        UnNumber = unNumber;
        StorageConditions = storageConditions;
        HandlingNotes = handlingNotes;
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public string ImdgClass { get; private set; } = null!;

    public string UnNumber { get; private set; } = null!;

    public string StorageConditions { get; private set; } = null!;

    public string? HandlingNotes { get; private set; }

    internal static CargoSafetyInfo Declare(
        Guid cargoBookingId,
        string imdgClass,
        string unNumber,
        string storageConditions,
        string? handlingNotes)
    {
        return new CargoSafetyInfo(
            Guid.NewGuid(),
            cargoBookingId,
            imdgClass,
            unNumber,
            storageConditions,
            handlingNotes);
    }
}
