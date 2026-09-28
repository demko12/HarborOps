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

internal sealed record CargoContainerList(IReadOnlyList<CargoContainerRow> Items, int TotalCount = 0, int PageNumber = 1, int PageSize = 15, int LoadedCount = 0, int? MatchingCount = null);

internal sealed record CargoContainerFilter(
    string? Search = null,
    string? Status = null,
    string? ContainerType = null,
    string? PortOfLoadingCode = null,
    string? PortOfDischargeCode = null)
{
    internal static CargoContainerFilter None { get; } = new();

    internal bool IsEmpty => this == None;

    internal static CargoContainerFilter Create(
        string? search,
        string? status,
        string? containerType,
        string? portOfLoadingCode,
        string? portOfDischargeCode)
    {
        return new CargoContainerFilter(
            Normalize(search),
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            Normalize(containerType),
            Normalize(portOfLoadingCode),
            Normalize(portOfDischargeCode));
    }

    internal IReadOnlyList<string> QueryParts()
    {
        var parts = new List<string>();

        AddQueryPart(parts, "search", Search);
        AddQueryPart(parts, "status", Status);
        AddQueryPart(parts, "containerType", ContainerType);
        AddQueryPart(parts, "portOfLoadingCode", PortOfLoadingCode);
        AddQueryPart(parts, "portOfDischargeCode", PortOfDischargeCode);

        return parts;
    }

    private static void AddQueryPart(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}

internal sealed record CargoManifestPrintFilters(
    string? Search,
    string? Status,
    string? ContainerType,
    string? PortOfLoadingCode,
    string? PortOfDischargeCode);

internal sealed record CargoManifestPrintRow(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    string Status,
    string? PortOfLoadingCode,
    string? PortOfDischargeCode,
    decimal? WeightKg,
    Guid? VesselId);

internal sealed record CargoManifestPrintView(
    Guid CargoBookingId,
    string BookingNumber,
    DateTime GeneratedAtUtc,
    CargoManifestPrintFilters Filters,
    bool IsFiltered,
    int MatchingCount,
    int BookingContainerCount,
    decimal KnownWeightKg,
    int KnownWeightCount,
    int UnknownWeightCount,
    int RowLimit,
    IReadOnlyList<CargoManifestPrintRow> Rows);

internal sealed record CargoFileDownloadResult(bool Ok, int Status, string? Message, int? Count);

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

internal sealed record BillOfLadingContainerRow(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    bool IsSnapshotted);

internal sealed record BillOfLadingEventRow(
    Guid BillOfLadingEventId,
    string Action,
    string FromStatus,
    string ToStatus,
    string? Notes,
    Guid ActorUserId,
    DateTime OccurredUtc);

internal sealed record BillOfLadingRow(
    Guid BillOfLadingId,
    Guid CargoBookingId,
    string BillNumber,
    string Status,
    string ShipperName,
    string ShipperAddress,
    string ConsigneeName,
    string ConsigneeAddress,
    string? NotifyPartyName,
    string? NotifyPartyAddress,
    string PlaceOfReceipt,
    string PortOfLoading,
    string PortOfDischarge,
    string PlaceOfDelivery,
    string FreightTerms,
    string CargoMarks,
    string CargoDescription,
    int PackageCount,
    decimal WeightKg,
    string IssuePlace,
    DateOnly RequestedIssueDate,
    Guid? IssuedByUserId,
    DateTime? IssuedAtUtc,
    Guid? VoidedByUserId,
    DateTime? VoidedAtUtc,
    string? VoidReason,
    string? SnapshotBookingNumber,
    Guid? SnapshotVoyageId,
    string? SnapshotCargoType,
    string? SnapshotBookingDescription,
    int? SnapshotBookingPackageCount,
    decimal? SnapshotBookingWeightKg,
    string? SnapshotBookingStatus,
    bool IsEditable,
    bool CanIssue,
    string? IssueBlockedReason,
    DateTime CreatedUtc,
    IReadOnlyList<BillOfLadingContainerRow> Containers);

internal sealed record BillOfLadingListRow(
    Guid BillOfLadingId,
    string BillNumber,
    string Status,
    string ShipperName,
    string ConsigneeName,
    string PortOfLoading,
    string PortOfDischarge,
    int ContainerCount,
    Guid? IssuedByUserId,
    DateTime? IssuedAtUtc,
    DateTime? VoidedAtUtc,
    DateTime CreatedUtc);

internal sealed record BillOfLadingList(
    Guid CargoBookingId,
    string BookingNumber,
    string BookingStatus,
    IReadOnlyList<BillOfLadingListRow> BillsOfLading);

internal sealed record BillOfLadingView(
    BillOfLadingRow BillOfLading,
    IReadOnlyList<BillOfLadingEventRow> History);

internal sealed record BillOfLadingPrintRow(
    Guid BillOfLadingId,
    string BillNumber,
    string Status,
    string? Watermark,
    string ShipperName,
    string ShipperAddress,
    string ConsigneeName,
    string ConsigneeAddress,
    string? NotifyPartyName,
    string? NotifyPartyAddress,
    string PlaceOfReceipt,
    string PortOfLoading,
    string PortOfDischarge,
    string PlaceOfDelivery,
    string FreightTerms,
    string CargoMarks,
    string CargoDescription,
    int PackageCount,
    decimal WeightKg,
    string IssuePlace,
    DateOnly RequestedIssueDate,
    string? BookingNumber,
    Guid? IssuedByUserId,
    DateTime? IssuedAtUtc,
    Guid? VoidedByUserId,
    DateTime? VoidedAtUtc,
    string? VoidReason,
    IReadOnlyList<BillOfLadingContainerRow> Containers);

internal sealed record SaveBillOfLadingWebRequest(
    string BillNumber,
    string ShipperName,
    string ShipperAddress,
    string ConsigneeName,
    string ConsigneeAddress,
    string? NotifyPartyName,
    string? NotifyPartyAddress,
    string PlaceOfReceipt,
    string PortOfLoading,
    string PortOfDischarge,
    string PlaceOfDelivery,
    string FreightTerms,
    string CargoMarks,
    string CargoDescription,
    int PackageCount,
    decimal WeightKg,
    string IssuePlace,
    DateOnly RequestedIssueDate,
    IReadOnlyList<Guid> ContainerIds);

internal sealed record VoidBillOfLadingWebRequest(string Reason);

internal sealed record ReeferProfileRow(
    Guid ContainerId,
    decimal MinimumCelsius,
    decimal MaximumCelsius,
    decimal TargetCelsius,
    int Version,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc);

internal sealed record ReeferProfileChangeRow(
    Guid Id,
    int Version,
    decimal? FromMinimumCelsius,
    decimal? FromMaximumCelsius,
    decimal? FromTargetCelsius,
    decimal ToMinimumCelsius,
    decimal ToMaximumCelsius,
    decimal ToTargetCelsius,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredUtc);

internal sealed record ReeferProfileView(
    Guid ContainerId,
    string ContainerNumber,
    ReeferProfileRow? Profile,
    IReadOnlyList<ReeferProfileChangeRow> Changes,
    int OpenExcursionCount,
    decimal? LatestInspectionTemperatureCelsius);

internal sealed record SaveReeferProfileWebRequest(
    decimal MinimumCelsius,
    decimal MaximumCelsius,
    decimal TargetCelsius,
    string? Reason);

internal sealed record ReeferReadingRow(
    Guid Id,
    Guid ContainerId,
    decimal TemperatureCelsius,
    DateTime MeasuredAtUtc,
    DateTime RecordedAtUtc,
    string? Notes,
    Guid RecordedByUserId,
    decimal ThresholdMinimumCelsius,
    decimal ThresholdMaximumCelsius,
    decimal ThresholdTargetCelsius,
    int ThresholdProfileVersion,
    string Status,
    bool IsWithinThresholds,
    Guid? CorrectsReadingId,
    Guid? SupersededByReadingId,
    string? CorrectionReason,
    Guid? CorrectedByUserId,
    DateTime? CorrectedAtUtc,
    string? VoidReason,
    Guid? VoidedByUserId,
    DateTime? VoidedAtUtc);

internal sealed record ReeferReadingPage(
    IReadOnlyList<ReeferReadingRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record RecordReeferReadingWebRequest(
    decimal TemperatureCelsius,
    DateTime MeasuredAtUtc,
    string? Notes);

internal sealed record CorrectReeferReadingWebRequest(
    decimal TemperatureCelsius,
    DateTime MeasuredAtUtc,
    string? Notes,
    string Reason);

internal sealed record VoidReeferReadingWebRequest(string Reason);

internal sealed record ReeferExcursionRow(
    Guid Id,
    Guid ContainerId,
    Guid ReadingId,
    string Direction,
    decimal TemperatureCelsius,
    decimal ThresholdMinimumCelsius,
    decimal ThresholdMaximumCelsius,
    DateTime MeasuredAtUtc,
    DateTime DetectedAtUtc,
    string Status,
    string? Disposition,
    string? Explanation,
    Guid? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    Guid? SupersededByExcursionId,
    DateTime? SupersededAtUtc);

internal sealed record RecordedReeferReading(
    ReeferReadingRow Reading,
    ReeferExcursionRow? Excursion);

internal sealed record ReeferExcursionList(
    IReadOnlyList<ReeferExcursionRow> Items,
    int OpenCount);

internal sealed record ResolveReeferExcursionWebRequest(
    string Disposition,
    string Explanation);
