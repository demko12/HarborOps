namespace HarborOps.Web.Api.Voyages;

internal sealed record VoyageListItemModel(
    Guid VoyageId,
    string VoyageNumber,
    Guid VesselId,
    string DeparturePortCode,
    string ArrivalPortCode,
    string Status,
    DateTime PlannedDeparture,
    DateTime PlannedArrival,
    DateTime? ActualDeparture,
    DateTime? ActualArrival,
    string? FirstStopPortName,
    string? LastStopPortName);

internal sealed record VoyagePage(
    IReadOnlyList<VoyageListItemModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record ScheduleVoyageWebRequest(
    string VoyageNumber,
    Guid VesselId,
    string DeparturePortCode,
    string ArrivalPortCode,
    DateTime PlannedDepartureUtc,
    DateTime PlannedArrivalUtc);

internal sealed record RecordDepartureWebRequest(DateTime ActualDepartureUtc);

internal sealed record RecordArrivalWebRequest(DateTime ActualArrivalUtc);

internal sealed record ScheduledVoyage(Guid Id);

internal sealed record VoyageTransition(Guid Id, string Status);

internal sealed record VoyagesApiResult<T>(T? Value, VoyagesApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record VoyagesApiFailure(
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

internal sealed record PortListItemModel(
    Guid PortId,
    string Name,
    string Country,
    string? City,
    string? UnLocode,
    decimal? Latitude,
    decimal? Longitude);

internal sealed record PortPage(
    IReadOnlyList<PortListItemModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record PortLookupItemModel(
    Guid PortId,
    string Name,
    string Country,
    string? UnLocode);

internal sealed record PortLookupResult(IReadOnlyList<PortLookupItemModel> Items);

internal sealed record RegisterPortWebRequest(
    string? Name,
    string? Country,
    string? City,
    string? UnLocode,
    decimal? Latitude,
    decimal? Longitude);

internal sealed record RegisteredPort(Guid Id);

internal sealed record BerthListItemModel(
    Guid BerthId,
    Guid PortId,
    string Name,
    decimal Length,
    decimal MaxDraft,
    string Status);

internal sealed record BerthPage(
    IReadOnlyList<BerthListItemModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record RegisterBerthWebRequest(
    Guid? PortId,
    string? Name,
    decimal? Length,
    decimal? MaxDraft,
    string? Status);

internal sealed record RegisteredBerth(Guid Id);

internal sealed record RouteListItemModel(
    Guid RouteId,
    string Name,
    string? Description,
    decimal EstimatedDistance,
    int EstimatedDuration,
    int StopCount);

internal sealed record RoutePage(
    IReadOnlyList<RouteListItemModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record RouteStopModel(
    Guid PortId,
    string PortName,
    string PortCountry,
    string? UnLocode,
    int StopOrder,
    decimal DistanceFromPrevious,
    int EstimatedDurationFromPrevious);

internal sealed record RouteDetails(
    Guid RouteId,
    string Name,
    string? Description,
    decimal EstimatedDistance,
    int EstimatedDuration,
    IReadOnlyList<RouteStopModel> Stops);

internal sealed record RegisterRouteStopWebRequest(
    Guid? PortId,
    int StopOrder,
    decimal? DistanceFromPrevious,
    int? EstimatedDurationFromPrevious);

internal sealed record RegisterRouteWebRequest(
    string? Name,
    string? Description,
    decimal? EstimatedDistance,
    int? EstimatedDuration,
    IReadOnlyList<RegisterRouteStopWebRequest> Stops);

internal sealed record RegisteredRoute(Guid Id);

internal sealed record PortNameItemModel(Guid PortId, string Name, string Country);

internal sealed record PortNamesPayload(IReadOnlyList<PortNameItemModel> Ports);

internal sealed record VoyageItineraryStopModel(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string PortCountry,
    string? UnLocode,
    Guid? BerthId,
    string? BerthName,
    DateTime Eta,
    DateTime Etd,
    DateTime? Ata,
    DateTime? Atd,
    string Status,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? ClearanceStatus = null,
    int ClearanceRequiredItemCount = 0,
    int ClearanceRequiredResolvedCount = 0,
    int ClearanceOverdueRequiredItemCount = 0,
    int PortCallActivityCount = 0,
    string? PortCallStatementStatus = null);

internal sealed record VoyageDelayModel(
    Guid DelayId,
    Guid? VoyageStopId,
    string Reason,
    string? Description,
    int Duration,
    DateTime CreatedAt);

internal sealed record VoyageItinerary(
    Guid VoyageId,
    Guid VesselId,
    IReadOnlyList<VoyageItineraryStopModel> Stops,
    IReadOnlyList<VoyageDelayModel> Delays);

internal sealed record PlanVoyageItineraryStopWebRequest(
    int? StopOrder,
    Guid? PortId,
    decimal? DistanceFromPrevious,
    int? EstimatedDurationFromPrevious,
    DateTime? Eta,
    DateTime? Etd);

internal sealed record PlanVoyageItineraryWebRequest(
    Guid? RouteId,
    IReadOnlyList<PlanVoyageItineraryStopWebRequest> Stops);

internal sealed record PlannedVoyageStopModel(Guid StopId, int StopOrder, Guid PortId, DateTime Eta, DateTime Etd);

internal sealed record PlanVoyageItineraryResult(Guid VoyageId, IReadOnlyList<PlannedVoyageStopModel> Stops);

internal sealed record AssignBerthWebRequest(Guid? BerthId);

internal sealed record AssignBerthResult(Guid StopId, Guid BerthId, DateTime StartTime, DateTime EndTime, string ScheduleStatus);

internal sealed record BerthAvailabilityModel(Guid BerthId, string BerthName, decimal Length, decimal MaxDraft, bool IsAvailableForRequestedWindow, DateTime? NearestAvailableFromUtc, DateTime? NearestAvailableToUtc);

internal sealed record BerthAvailability(Guid PortId, DateTime FromUtc, DateTime ToUtc, DateTime SearchHorizonEndsUtc, IReadOnlyList<BerthAvailabilityModel> Berths);

internal sealed record ReplaceBerthAllocationWebRequest(Guid? BerthId, DateTime? PlannedArrivalUtc, DateTime? PlannedDepartureUtc, string? Reason);

internal sealed record ReplacedBerthAllocation(Guid StopId, Guid BerthId, DateTime PlannedArrivalUtc, DateTime PlannedDepartureUtc, DateTime ChangedAtUtc);

internal sealed record BerthAllocationHistoryItemModel(Guid ChangeId, Guid? PreviousBerthId, string? PreviousBerthName, Guid NewBerthId, string NewBerthName, DateTime PreviousPlannedArrivalUtc, DateTime PreviousPlannedDepartureUtc, DateTime NewPlannedArrivalUtc, DateTime NewPlannedDepartureUtc, string Reason, Guid ChangedByUserId, DateTime ChangedAtUtc);

internal sealed record BerthAllocationHistory(Guid VoyageId, Guid StopId, IReadOnlyList<BerthAllocationHistoryItemModel> Changes);

internal sealed record RecordStopArrivalWebRequest(DateTime ActualArrivalUtc);

internal sealed record RecordStopDepartureWebRequest(DateTime ActualDepartureUtc);

internal sealed record StopTransition(Guid StopId, string Status);

internal sealed record RecordDelayWebRequest(
    string? Reason,
    string? Description,
    int? Duration,
    Guid? VoyageStopId);

internal sealed record RecordedDelay(
    Guid DelayId,
    Guid VoyageId,
    Guid? VoyageStopId,
    string Reason,
    string? Description,
    int Duration,
    DateTime CreatedAt);

internal sealed record AmendVoyageScheduleWebRequest(
    Guid? RouteId,
    Guid? DeparturePortId,
    Guid? ArrivalPortId,
    DateTime? PlannedDepartureUtc,
    DateTime? PlannedArrivalUtc,
    string? Reason);

internal sealed record CancelVoyageWebRequest(string? Reason);

internal sealed record VoyageScheduleSummaryModel(
    Guid VoyageId,
    string VoyageNumber,
    string Status,
    Guid? RouteId,
    string? RouteName,
    Guid? DeparturePortId,
    string? DeparturePortName,
    Guid? ArrivalPortId,
    string? ArrivalPortName,
    DateTime PlannedDeparture,
    DateTime PlannedArrival);

internal sealed record VoyageScheduleChangeModel(
    Guid ChangeId,
    string ChangeType,
    string Reason,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc,
    string PreviousStatus,
    string NewStatus,
    Guid? PreviousRouteId,
    string? PreviousRouteName,
    Guid? NewRouteId,
    string? NewRouteName,
    Guid? PreviousDeparturePortId,
    string? PreviousDeparturePortName,
    Guid? NewDeparturePortId,
    string? NewDeparturePortName,
    Guid? PreviousArrivalPortId,
    string? PreviousArrivalPortName,
    Guid? NewArrivalPortId,
    string? NewArrivalPortName,
    DateTime PreviousPlannedDeparture,
    DateTime NewPlannedDeparture,
    DateTime PreviousPlannedArrival,
    DateTime NewPlannedArrival);

internal sealed record VoyageSchedule(
    VoyageScheduleSummaryModel Voyage,
    IReadOnlyList<VoyageScheduleChangeModel> Changes);

internal sealed record DelayImpactStopModel(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string Status,
    DateTime CurrentEta,
    DateTime CurrentEtd,
    DateTime ProposedEta,
    DateTime ProposedEtd,
    int EtaDeltaMinutes,
    int EtdDeltaMinutes);

internal sealed record BerthConflictWarningModel(
    Guid StopId,
    int StopOrder,
    Guid BerthId,
    string BerthName,
    DateTime ConflictStart,
    DateTime ConflictEnd);

internal sealed record DelayImpactPreview(
    Guid VoyageId,
    Guid DelayId,
    int DelayMinutes,
    DateTime PlannedArrival,
    DateTime ProposedArrival,
    int PlannedArrivalDeltaMinutes,
    IReadOnlyList<DelayImpactStopModel> Stops,
    IReadOnlyList<BerthConflictWarningModel> Warnings);

internal sealed record ApplyDelayImpactStopWebRequest(
    Guid VoyageStopId,
    DateTime ExpectedEta,
    DateTime ExpectedEtd,
    DateTime Eta,
    DateTime Etd);

internal sealed record ApplyDelayImpactWebRequest(
    string? Reason,
    IReadOnlyList<ApplyDelayImpactStopWebRequest> Stops);

internal sealed record EstimateRevisionStopChangeModel(
    Guid StopId,
    int StopOrder,
    string PortName,
    DateTime PreviousEta,
    DateTime PreviousEtd,
    DateTime NewEta,
    DateTime NewEtd);

internal sealed record VoyageEstimateRevisionModel(
    Guid RevisionBatchId,
    Guid DelayId,
    string Reason,
    Guid RevisedByUserId,
    DateTime RevisedAtUtc,
    bool AlertCreated,
    IReadOnlyList<EstimateRevisionStopChangeModel> Changes);

internal sealed record VoyageEstimateStopModel(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string Status,
    DateTime PlannedEta,
    DateTime PlannedEtd,
    DateTime? RevisedEta,
    DateTime? RevisedEtd,
    DateTime? Ata,
    DateTime? Atd);

internal sealed record VoyageEstimateRevisions(
    Guid VoyageId,
    IReadOnlyList<VoyageEstimateStopModel> Stops,
    IReadOnlyList<VoyageEstimateRevisionModel> Revisions);

internal sealed record EstimateRevisionAlert(Guid RevisionBatchId, bool AlertCreated);

internal sealed record PortCallClearanceProgressModel(
    int TotalItemCount,
    int RequiredItemCount,
    int RequiredResolvedCount,
    int RequiredPendingCount,
    int OverdueRequiredItemCount,
    int OptionalItemCount,
    int OptionalPendingCount,
    int ClearedCount,
    int WaivedCount,
    bool CanComplete);

internal sealed record PortCallClearanceItemModel(
    Guid ItemId,
    string Category,
    string Title,
    bool IsRequired,
    DateTime? DueAtUtc,
    string? AuthorityReference,
    Guid? EvidenceDocumentId,
    string Status,
    bool IsOverdue,
    string? DecisionReason,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    Guid AddedByUserId,
    DateTime AddedAtUtc);

internal sealed record PortCallClearanceEventModel(
    Guid EventId,
    Guid? ItemId,
    int Sequence,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc);

internal sealed record PortCallClearance(
    Guid VoyageId,
    Guid StopId,
    Guid ClearanceId,
    string Status,
    Guid? CompletedByUserId,
    DateTime? CompletedAtUtc,
    DateTime EvaluatedAtUtc,
    PortCallClearanceProgressModel Progress,
    IReadOnlyList<PortCallClearanceItemModel> Items,
    IReadOnlyList<PortCallClearanceEventModel> History);

internal sealed record AddClearanceItemWebRequest(
    string? Category,
    string? Title,
    bool IsRequired,
    DateTime? DueAtUtc,
    string? AuthorityReference,
    Guid? EvidenceDocumentId);

internal sealed record UpdateClearanceItemWebRequest(
    string? Category,
    string? Title,
    bool IsRequired,
    DateTime? DueAtUtc,
    string? AuthorityReference,
    Guid? EvidenceDocumentId);

internal sealed record ClearClearanceItemWebRequest(
    string? AuthorityReference,
    Guid? EvidenceDocumentId);

internal sealed record WaiveClearanceItemWebRequest(string? Reason);

internal sealed record ReopenClearanceWebRequest(string? Reason);

internal sealed record AddedClearanceItem(
    Guid ItemId,
    Guid ClearanceId,
    string Status,
    Guid AddedByUserId,
    DateTime AddedAtUtc);

internal sealed record ClearanceItemDecision(
    Guid ItemId,
    string Status,
    Guid DecidedByUserId,
    DateTime DecidedAtUtc);

internal sealed record ClearanceStatusChange(
    Guid ClearanceId,
    Guid StopId,
    string Status,
    Guid ActorUserId,
    DateTime OccurredAtUtc);

internal sealed record ClearanceItemSaved();

internal sealed record PortCallActivityModel(
    Guid ActivityId,
    int Sequence,
    string ActivityType,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    string Status,
    int Revision,
    bool IsLocked,
    Guid RecordedByUserId,
    DateTime RecordedAtUtc,
    Guid LastChangedByUserId,
    DateTime LastChangedAtUtc,
    Guid Version);

internal sealed record PortCallActivityRevisionModel(
    int Revision,
    string ChangeType,
    string ActivityType,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    string? Reason,
    Guid ActorUserId,
    string? ActorName,
    DateTime ChangedAtUtc);

internal sealed record PortCallActivityDetails(
    Guid VoyageId,
    Guid StopId,
    PortCallActivityModel Activity,
    IReadOnlyList<PortCallActivityRevisionModel> History);

internal sealed record PortCallLogEntryModel(
    int Position,
    string Source,
    Guid? SourceId,
    int? SourceRevision,
    string Label,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    int? DurationMinutes,
    bool IsOutsidePortCall);

internal sealed record PortCallStatementSummaryModel(
    Guid StatementId,
    string Status,
    Guid? FinalizedByUserId,
    DateTime? FinalizedAtUtc);

internal sealed record PortCallTimeline(
    Guid VoyageId,
    Guid StopId,
    string StopStatus,
    string PortName,
    string? PortTimeZone,
    string? BerthName,
    DateTime? ActualArrivalUtc,
    DateTime? ActualDepartureUtc,
    bool CanRecordActivities,
    PortCallStatementSummaryModel? Statement,
    IReadOnlyList<PortCallLogEntryModel> Entries,
    IReadOnlyList<PortCallActivityModel> Activities);

internal sealed record PortCallStatementBlockerModel(string Code, string Message, Guid? ActivityId);

internal sealed record PortCallStatementModel(
    Guid StatementId,
    string DocumentNumber,
    Guid VoyageId,
    Guid StopId,
    string Status,
    Guid Version,
    string? SourceVersion,
    string VoyageNumber,
    int StopOrder,
    string PortName,
    string? PortUnLocode,
    string? PortTimeZone,
    string? BerthName,
    DateTime? ActualArrivalUtc,
    DateTime? ActualDepartureUtc,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid? FinalizedByUserId,
    string? FinalizedByName,
    DateTime? FinalizedAtUtc,
    bool CanFinalize,
    IReadOnlyList<PortCallStatementBlockerModel> Blockers,
    IReadOnlyList<PortCallLogEntryModel> Entries);

internal sealed record RecordPortCallActivityWebRequest(
    string ActivityType,
    DateTimeOffset OccurredAt,
    DateTimeOffset? EndedAt,
    string? Notes);

internal sealed record CorrectPortCallActivityWebRequest(
    string ActivityType,
    DateTimeOffset OccurredAt,
    DateTimeOffset? EndedAt,
    string? Notes,
    string Reason,
    Guid Version);

internal sealed record VoidPortCallActivityWebRequest(string Reason, Guid Version);

internal sealed record FinalizePortCallStatementWebRequest(Guid Version, string? SourceVersion);

internal sealed record PortCallActivityChange(
    Guid ActivityId,
    string Status,
    int Revision,
    Guid Version,
    Guid ActorUserId,
    DateTime ChangedAtUtc);
