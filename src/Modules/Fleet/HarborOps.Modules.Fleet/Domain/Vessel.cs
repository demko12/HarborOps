namespace HarborOps.Modules.Fleet.Domain;

internal sealed class Vessel
{
    internal const int FlagStateMaxLength = 100;
    internal const int ImoNumberLength = 7;
    internal const int NameMaxLength = 200;
    internal const int MmsiMaxLength = 20;
    internal const int CallSignMaxLength = 20;
    internal const int PortOfRegistryMaxLength = 200;
    internal const int ClassificationSocietyMaxLength = 200;
    internal const int LifecycleStageMaxLength = 50;
    internal const int CurrentStatusMaxLength = 50;
    internal const int MsmdIdMaxLength = 50;

    private Vessel()
    {
    }

    private Vessel(
        Guid id,
        Guid companyId,
        Guid vesselTypeId,
        string name,
        string imoNumber,
        string flagState,
        string currentStatus,
        DateTime createdUtc)
    {
        Id = id;
        CompanyId = companyId;
        VesselTypeId = vesselTypeId;
        Name = name;
        ImoNumber = imoNumber;
        FlagState = flagState;
        CurrentStatus = currentStatus;
        CreatedAtUtc = createdUtc;
        UpdatedAtUtc = createdUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid VesselTypeId { get; private set; }

    public string Name { get; private set; } = null!;

    public string ImoNumber { get; private set; } = null!;

    public string? Mmsi { get; private set; }

    public string? CallSign { get; private set; }

    public string FlagState { get; private set; } = null!;

    public string? PortOfRegistry { get; private set; }

    public string? ClassificationSociety { get; private set; }

    public decimal? Dwt { get; private set; }

    public decimal? Gt { get; private set; }

    public decimal? Loa { get; private set; }

    public decimal? Beam { get; private set; }

    public decimal? Draft { get; private set; }

    public decimal? CurrentLatitude { get; private set; }

    public decimal? CurrentLongitude { get; private set; }

    public string? LifecycleStage { get; private set; }

    public string CurrentStatus { get; private set; } = null!;

    public string? MsmdId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    internal static Vessel Register(
        Guid companyId,
        Guid vesselTypeId,
        string name,
        string imoNumber,
        string flagState,
        string currentStatus)
    {
        return new Vessel(
            Guid.NewGuid(),
            companyId,
            vesselTypeId,
            name,
            imoNumber,
            flagState,
            currentStatus,
            DateTime.UtcNow);
    }

    internal void UpdatePosition(decimal latitude, decimal longitude)
    {
        CurrentLatitude = latitude;
        CurrentLongitude = longitude;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    internal void ChangeStatus(string currentStatus)
    {
        CurrentStatus = currentStatus;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    internal void UpdateSpecifications(
        decimal? dwt,
        decimal? gt,
        decimal? loa,
        decimal? beam,
        decimal? draft)
    {
        Dwt = dwt;
        Gt = gt;
        Loa = loa;
        Beam = beam;
        Draft = draft;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
