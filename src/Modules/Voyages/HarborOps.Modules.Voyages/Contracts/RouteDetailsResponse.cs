namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteDetailsResponse(
    Guid RouteId,
    string Name,
    string? Description,
    decimal EstimatedDistance,
    int EstimatedDuration,
    IReadOnlyList<RouteStopItem> Stops);
