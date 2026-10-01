using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselPositionListItem(
    Guid VesselId,
    string Name,
    string ImoNumber,
    string CurrentStatus,
    Guid? FleetId,
    string? FleetName,
    decimal? CurrentLatitude,
    decimal? CurrentLongitude,
    DateTime? LastPositionReportedAtUtc,
    bool IsPositionStale,
    string? PositionSource = null,
    string? Mmsi = null)
{
    [JsonIgnore]
    public Guid Id => VesselId;

    [JsonIgnore]
    public bool HasPosition => CurrentLatitude is not null && CurrentLongitude is not null;
}
