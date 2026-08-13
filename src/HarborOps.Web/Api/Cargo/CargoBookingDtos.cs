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
}
