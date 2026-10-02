namespace HarborOps.Web.Api.Fleet;

internal sealed record VesselPageResponse(
    IReadOnlyList<VesselListItemResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record VesselListItemResponse(
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
    DateTime? LastPositionReportedAtUtc = null);

internal sealed record VesselLookupItem(Guid Id, string Name);

internal sealed record VesselLookupWebRequest(IReadOnlyCollection<Guid> VesselIds);

internal sealed record VesselLookupWebResponse(IReadOnlyList<VesselLookupItem> Vessels);

internal sealed record FleetDirectoryWebModel(
    IReadOnlyList<FleetDirectoryItemWebModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

internal sealed record FleetDirectoryItemWebModel(
    Guid FleetId,
    string Name,
    string Description,
    DateTime CreatedAtUtc,
    int ActiveVesselCount);

internal sealed record CreateFleetWebRequest(string Name, string Description);

internal sealed record CreateFleetWebResponse(Guid FleetId);

internal sealed record FleetDetailsWebModel(
    Guid FleetId,
    string Name,
    string Description,
    DateTime CreatedAtUtc,
    IReadOnlyList<FleetAssignedVesselWebModel> ActiveVessels);

internal sealed record FleetAssignedVesselWebModel(
    Guid VesselId,
    string Name,
    string ImoNumber,
    string CurrentStatus,
    DateTime AssignedFromUtc);

internal sealed record FleetAssignmentHistoryWebModel(IReadOnlyList<FleetAssignmentHistoryItemWebModel> Items);

internal sealed record FleetAssignmentHistoryItemWebModel(
    Guid FleetId,
    string FleetName,
    DateTime AssignedFromUtc,
    DateTime? AssignedToUtc,
    bool IsActive);

internal sealed record AssignFleetWebRequest(Guid FleetId);

internal sealed record FleetApiResult<T>(T? Value, FleetApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record FleetApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;
}

internal sealed record VesselDetailsWebModel(
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
    string? MsmdId,
    string CurrentStatus,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? FleetId,
    string? FleetName,
    IReadOnlyList<VesselStatusHistoryWebModel> StatusHistory);

internal sealed record VesselStatusHistoryWebModel(
    Guid Id,
    string Status,
    string ChangedBy,
    string? Notes,
    DateTime ChangedAtUtc);

internal sealed record RecordVesselStatusChangeWebRequest(
    string? Status,
    string? Notes);

internal sealed record UpdateVesselRegistryDetailsWebRequest(
    Guid? VesselTypeId,
    string? Name,
    string? Mmsi,
    string? CallSign,
    string? FlagState,
    string? PortOfRegistry,
    string? ClassificationSociety,
    string? LifecycleStage,
    string? MsmdId);

internal sealed record UpdateVesselSpecificationsWebRequest(
    decimal? Dwt,
    decimal? Gt,
    decimal? Loa,
    decimal? Beam,
    decimal? Draft);

internal sealed record VesselTypesWebModel(IReadOnlyList<VesselTypeWebModel> VesselTypes);

internal sealed record VesselTypeWebModel(Guid Id, string Name, string Code);

internal sealed record RecordVesselStatusChangeWebResponse(
    Guid Id,
    Guid VesselId,
    string Status,
    string ChangedBy,
    string? Notes,
    DateTime ChangedAtUtc);

internal static class FleetStatusOptions
{
    internal static readonly IReadOnlyList<string> All =
    [
        "Active",
        "En Route",
        "At Port",
        "Maintenance",
        "Inactive",
    ];
}

internal sealed record VesselPositionsWebModel(
    IReadOnlyList<VesselPositionWebModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    double StaleAfterHours,
    DateTime EvaluatedAtUtc,
    AisTrackingWebModel? AisTracking = null);

internal sealed record AisTrackingWebModel(string ConnectionState, DateTime? LastPositionReceivedAtUtc);

internal sealed record VesselPositionWebModel(
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
    internal bool HasPosition => CurrentLatitude is not null && CurrentLongitude is not null;
}

internal sealed record RecordVesselPositionWebRequest(decimal? Latitude, decimal? Longitude);

internal sealed record RecordVesselPositionWebResponse(
    Guid VesselId,
    decimal Latitude,
    decimal Longitude,
    DateTime ReportedAtUtc);

internal sealed record VesselPositionHistoryWebModel(IReadOnlyList<VesselPositionHistoryItemWebModel> Items, int PageNumber, int PageSize, int TotalCount);
internal sealed record VesselPositionHistoryItemWebModel(Guid Id, decimal Latitude, decimal Longitude, DateTime ReportedAtUtc, Guid ReportedByUserId, string? ReportedByDisplayName);
