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
}
