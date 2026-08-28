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
    int TotalCount,
    int HazardousCount);

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

internal sealed record CargoContainerRow(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    string Status,
    Guid CargoBookingId,
    Guid? VesselId);

internal sealed record CargoContainerList(IReadOnlyList<CargoContainerRow> Items);

internal sealed record CargoOperationRow(
    Guid OperationId,
    string OperationType,
    string OperationState,
    DateTime PlannedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    Guid ResponsibleUserId);

internal sealed record CargoOperationList(IReadOnlyList<CargoOperationRow> Items);

internal sealed record AddCargoContainerWebRequest(string ContainerNumber, string ContainerType);

internal sealed record AddedCargoContainer(Guid Id, string ContainerNumber, string Status);

internal sealed record PlanCargoOperationWebRequest(string OperationType, DateTime PlannedAtUtc);

internal sealed record PlannedCargoOperation(Guid Id, string OperationType, string OperationState);

internal sealed record CargoOperationTransition(
    Guid OperationId,
    string OperationState,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc);
