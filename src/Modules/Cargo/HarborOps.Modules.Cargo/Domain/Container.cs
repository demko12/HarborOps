namespace HarborOps.Modules.Cargo.Domain;

internal sealed class Container
{
    internal const int ContainerNumberMaxLength = 20;
    internal const int ContainerTypeMaxLength = 50;
    internal const int StatusMaxLength = 50;

    private Container()
    {
    }

    private Container(
        Guid id,
        string containerNumber,
        string containerType,
        CargoStatus status,
        Guid? vesselId)
    {
        Id = id;
        ContainerNumber = containerNumber;
        ContainerType = containerType;
        Status = status;
        VesselId = vesselId;
    }

    public Guid Id { get; private set; }

    public string ContainerNumber { get; private set; } = null!;

    public string ContainerType { get; private set; } = null!;

    public CargoStatus Status { get; private set; }

    public Guid? VesselId { get; private set; }

    internal static Container Register(
        string containerNumber,
        string containerType,
        CargoStatus status,
        Guid? vesselId)
    {
        return new Container(
            Guid.NewGuid(),
            containerNumber,
            containerType,
            status,
            vesselId);
    }
}
