namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteListItem(
    Guid RouteId,
    string Name,
    string? Description,
    decimal EstimatedDistance,
    int EstimatedDuration,
    int StopCount);
