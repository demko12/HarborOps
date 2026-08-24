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
    DateTime? ActualArrival);

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

internal sealed record PortPage(
    IReadOnlyList<PortListItemModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record PortListItemModel(
    Guid PortId,
    string Name,
    string? City,
    string Country,
    string? UnLocode,
    string? ShipyardName);

internal sealed record PortLookupItemModel(Guid Id, string Name, string Country);

internal sealed record PortLookupPayload(IReadOnlyList<PortLookupItemModel> Ports);
