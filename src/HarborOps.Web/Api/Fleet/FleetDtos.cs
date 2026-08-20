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
    string? FleetName);

internal sealed record VesselLookupItem(Guid Id, string Name);

internal sealed record VesselLookupWebRequest(IReadOnlyCollection<Guid> VesselIds);

internal sealed record VesselLookupWebResponse(IReadOnlyList<VesselLookupItem> Vessels);

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
