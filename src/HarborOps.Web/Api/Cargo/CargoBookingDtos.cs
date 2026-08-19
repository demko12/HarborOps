namespace HarborOps.Web.Api.Cargo;

internal sealed record CargoBookingRow(
    Guid CargoBookingId,
    string BookingNumber,
    Guid VoyageId,
    string Description,
    string CargoType,
    decimal WeightKg,
    int PackageCount,
    bool IsHazardous,
    string Status,
    DateTime CreatedUtc);

internal sealed record CargoBookingPage(
    IReadOnlyList<CargoBookingRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record CargoBookingFilter(
    string? Search,
    string? Status,
    bool? IsHazardous,
    int PageNumber,
    int PageSize);

internal sealed record CargoBookingDetails(
    Guid CargoBookingId,
    string BookingNumber,
    Guid VoyageId,
    string Description,
    string CargoType,
    decimal WeightKg,
    int PackageCount,
    bool IsHazardous,
    string Status,
    string? NextStatus,
    DateTime CreatedUtc,
    CargoSafetyInformation? SafetyInfo,
    IReadOnlyList<CargoTrackingEventRow> TrackingEvents);

internal sealed record CargoSafetyInformation(
    Guid CargoSafetyInfoId,
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes);

internal sealed record CargoTrackingEventRow(
    Guid CargoTrackingEventId,
    string Status,
    DateTime OccurredUtc,
    string Location,
    Guid ResponsibleUserId);

internal sealed record RegisterCargoBookingWebRequest(
    string BookingNumber,
    Guid VoyageId,
    string Description,
    string CargoType,
    decimal WeightKg,
    int PackageCount,
    bool IsHazardous);

internal sealed record RegisteredCargoBooking(Guid Id);

internal sealed record DeclareCargoSafetyInfoWebRequest(
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes);

internal sealed record DeclaredCargoSafetyInfo(Guid Id);

internal sealed record RecordCargoTrackingEventWebRequest(
    string Status,
    DateTime OccurredUtc,
    string Location);

internal sealed record RecordedCargoTrackingEvent(Guid Id, string Status);

internal sealed record CargoApiResult<T>(T? Value, CargoApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record CargoApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;
}
