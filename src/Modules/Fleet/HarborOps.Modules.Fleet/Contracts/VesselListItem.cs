using System.Text.Json.Serialization;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselListItem(
    Guid VesselId,
    string Name,
    string ImoNumber,
    string FlagState,
    string CurrentStatus,
    Guid VesselTypeId,
    string VesselTypeName,
    Guid CompanyId,
    decimal? Dwt,
    DateTime UpdatedAtUtc,
    string? LifecycleStage,
    Guid? FleetId,
    string? FleetName,
    decimal? CurrentLatitude = null,
    decimal? CurrentLongitude = null,
    DateTime? LastPositionReportedAtUtc = null)
{
    [JsonIgnore]
    public Guid Id => VesselId;
}
