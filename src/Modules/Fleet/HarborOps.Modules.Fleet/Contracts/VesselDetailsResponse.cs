using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselDetailsResponse(
    Guid VesselId,
    Guid CompanyId,
    string Name,
    string ImoNumber,
    string? Mmsi,
    string? CallSign,
    string FlagState,
    Guid VesselTypeId,
    string VesselTypeName,
    decimal? Dwt,
    decimal? Gt,
    decimal? Loa,
    decimal? Beam,
    decimal? Draft,
    string? PortOfRegistry,
    string? ClassificationSociety,
    string? LifecycleStage,
    string CurrentStatus,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? FleetId,
    string? FleetName,
    IReadOnlyList<VesselStatusHistoryItem> StatusHistory);
