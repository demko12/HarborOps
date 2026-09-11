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
    IReadOnlyList<CargoTrackingEventRow> TrackingEvents,
    IReadOnlyList<ContainerControlRow>? ContainerControls);

internal sealed record CargoSafetyInformation(
    Guid CargoSafetyInfoId,
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes,
    string ReviewStatus,
    Guid? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? ReviewNotes);

internal sealed record CargoSafetyReviewEntryRow(
    Guid CargoSafetyReviewId,
    string FromStatus,
    string ToStatus,
    Guid ActorUserId,
    DateTime OccurredUtc,
    string? Notes);

internal sealed record CargoSafetyInformationView(
    CargoSafetyInformation Declaration,
    IReadOnlyList<CargoSafetyReviewEntryRow> Timeline);

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

internal sealed record UpdateCargoSafetyInfoWebRequest(
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes);

internal sealed record ReviewCargoSafetyInfoWebRequest(string Status, string? Notes);
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
    Guid? VesselId,
    string? PortOfLoadingCode = null,
    string? PortOfDischargeCode = null,
    decimal? WeightKg = null);

internal sealed record CargoContainerList(IReadOnlyList<CargoContainerRow> Items, int TotalCount = 0, int PageNumber = 1, int PageSize = 15, int LoadedCount = 0);

internal sealed record CargoOperationRow(
    Guid OperationId,
    string OperationType,
    string OperationState,
    DateTime PlannedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    Guid ResponsibleUserId);

internal sealed record CargoOperationList(IReadOnlyList<CargoOperationRow> Items);

internal sealed record AddCargoContainerWebRequest(string ContainerNumber, string ContainerType, string PortOfLoadingCode = "", string PortOfDischargeCode = "", decimal WeightKg = 0m);

internal sealed record AddedCargoContainer(Guid Id, string ContainerNumber, string Status);
internal sealed record UpdateCargoContainerWebRequest(string ContainerNumber, string ContainerType, string PortOfLoadingCode, string PortOfDischargeCode, decimal WeightKg);
internal sealed record UpdatedCargoContainer(Guid Id, string ContainerNumber, string ContainerType, string PortOfLoadingCode, string PortOfDischargeCode, decimal WeightKg);

internal sealed record ContainerManifestImportRow(int RowNumber, string? ContainerNumber, string? ContainerType, string? PortOfLoadingCode, string? PortOfDischargeCode, decimal? WeightKg, IReadOnlyDictionary<string, string[]> Errors);
internal sealed record ContainerManifestPreview(int TotalCount, int ValidCount, int InvalidCount, IReadOnlyList<ContainerManifestImportRow> Rows);
internal sealed record ImportedContainerManifest(int CreatedCount, IReadOnlyList<Guid> ContainerIds);

internal sealed record PlanCargoOperationWebRequest(string OperationType, DateTime PlannedAtUtc);

internal sealed record PlannedCargoOperation(Guid Id, string OperationType, string OperationState);

internal sealed record ContainerControlRow(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    bool IsReefer,
    string? SealNumber,
    string SealStatus,
    string State,
    string? LatestInspectionResult,
    string? LatestInspectionNotes,
    decimal? LatestInspectionTemperatureCelsius,
    DateTime? LatestInspectedAtUtc,
    Guid? LatestInspectedByUserId,
    string? StateReason,
    Guid? StateChangedByUserId,
    DateTime? StateChangedAtUtc,
    bool CanRelease,
    string? ReleaseBlockedReason);

internal sealed record ContainerControlEventRow(
    Guid ContainerControlEventId,
    string Action,
    string FromState,
    string ToState,
    string? SealNumber,
    string? InspectionResult,
    decimal? TemperatureCelsius,
    string? Notes,
    Guid ActorUserId,
    DateTime OccurredUtc);

internal sealed record ContainerControlView(
    ContainerControlRow Control,
    IReadOnlyList<ContainerControlEventRow> History);

internal sealed record AssignContainerSealWebRequest(string SealNumber);

internal sealed record BreakContainerSealWebRequest(string? Notes);

internal sealed record RecordContainerInspectionWebRequest(
    string Result,
    string? ConditionNotes,
    decimal? TemperatureCelsius);

internal sealed record ReleaseContainerWebRequest(string? Notes);

internal sealed record HoldContainerWebRequest(string Reason);

internal sealed record CargoOperationTransition(
    Guid OperationId,
    string OperationState,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc);
