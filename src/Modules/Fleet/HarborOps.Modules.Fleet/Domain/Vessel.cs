namespace HarborOps.Modules.Fleet.Domain;

internal sealed class Vessel
{
    internal const int FlagStateMaxLength = 100;
    internal const int ImoNumberLength = 7;
    internal const int NameMaxLength = 200;
    internal const int VesselTypeMaxLength = 100;

    private Vessel()
    {
    }

    private Vessel(
        Guid id,
        string name,
        string imoNumber,
        string flagState,
        string vesselType,
        DateTime createdUtc)
    {
        Id = id;
        Name = name;
        ImoNumber = imoNumber;
        FlagState = flagState;
        VesselType = vesselType;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string ImoNumber { get; private set; } = null!;

    public string FlagState { get; private set; } = null!;

    public string VesselType { get; private set; } = null!;

    public DateTime CreatedUtc { get; private set; }

    internal static Vessel Register(
        string name,
        string imoNumber,
        string flagState,
        string vesselType)
    {
        return new Vessel(
            Guid.NewGuid(),
            name,
            imoNumber,
            flagState,
            vesselType,
            DateTime.UtcNow);
    }
}
