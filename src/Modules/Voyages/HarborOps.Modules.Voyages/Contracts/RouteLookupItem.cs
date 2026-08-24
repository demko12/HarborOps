namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteLookupItem(
    Guid RouteId,
    string Name,
    decimal EstimatedDistance,
    int EstimatedDuration);
