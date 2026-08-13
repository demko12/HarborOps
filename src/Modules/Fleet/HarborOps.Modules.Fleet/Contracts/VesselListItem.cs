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
    string? FleetName)
{
    [JsonIgnore]
    public Guid Id => VesselId;
}
