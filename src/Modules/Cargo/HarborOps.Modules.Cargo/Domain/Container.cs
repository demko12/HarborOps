namespace HarborOps.Modules.Cargo.Domain;

internal sealed class Container
{
    internal const int ContainerNumberMaxLength = 20;
    internal const int ContainerTypeMaxLength = 50;
    internal const int StatusMaxLength = 50;

    private const char ReeferGroupCharacter = 'R';
    private const int ReeferGroupIndex = 2;
    private const int IsoSizeTypeLength = 4;
    private const string ReeferTypeName = "REEFER";

    private Container()
    {
    }

    private Container(
        Guid id,
        string containerNumber,
        string containerType,
        CargoStatus status,
        Guid? vesselId,
        Guid? cargoBookingId)
    {
        Id = id;
        ContainerNumber = containerNumber;
        ContainerType = containerType;
        Status = status;
        VesselId = vesselId;
        CargoBookingId = cargoBookingId;
    }

    public Guid Id { get; private set; }

    public string ContainerNumber { get; private set; } = null!;

    public string ContainerType { get; private set; } = null!;

    public CargoStatus Status { get; private set; }

    public Guid? VesselId { get; private set; }

    public Guid? CargoBookingId { get; private set; }

    internal bool IsReefer => IsReeferType(ContainerType);

    internal static string NormalizeNumber(string containerNumber)
    {
        ArgumentNullException.ThrowIfNull(containerNumber);

        return containerNumber.Trim().ToUpperInvariant();
    }

    internal static string NormalizeSealNumber(string sealNumber)
    {
        ArgumentNullException.ThrowIfNull(sealNumber);

        return string.Concat(sealNumber.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    internal static bool IsReeferType(string containerType)
    {
        ArgumentNullException.ThrowIfNull(containerType);

        var normalized = containerType.Trim().ToUpperInvariant();

        return normalized is ReeferTypeName
            || (normalized.Length >= IsoSizeTypeLength
                && normalized[ReeferGroupIndex] is ReeferGroupCharacter);
    }

    internal static Container Register(
        string containerNumber,
        string containerType,
        CargoStatus status,
        Guid? vesselId = null,
        Guid? cargoBookingId = null)
    {
        return new Container(
            Guid.NewGuid(),
            containerNumber,
            containerType,
            status,
            vesselId,
            cargoBookingId);
    }

    internal void ApplyOperationOutcome(CargoOperationType operationType)
    {
        Status = LoadUnloadOperation.OutcomeStatusFor(operationType);
    }
}
