namespace HarborOps.Modules.Voyages.Application;

internal sealed class RegisterRouteValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register route validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RouteStopPortNotFoundException(Guid portId)
    : Exception($"Route stop port '{portId}' was not found.")
{
    internal Guid PortId { get; } = portId;
}

internal sealed class SearchRoutesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search routes validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RouteLookupValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Route lookup validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RouteNotFoundException(Guid routeId)
    : Exception($"Route '{routeId}' was not found.")
{
    internal Guid RouteId { get; } = routeId;
}
