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
    string Status);

internal sealed record VoyageDelayModel(
    Guid DelayId,
    Guid? VoyageStopId,
    string Reason,
    string? Description,
    int Duration,
    DateTime CreatedAt);

internal sealed record VoyageItinerary(
    Guid VoyageId,
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
