namespace HarborOps.Modules.Voyages.Application;

internal sealed class AmendVoyageScheduleValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Amend voyage schedule validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CancelVoyageValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cancel voyage validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class VoyageRouteEndpointMismatchException(
    Guid routeId,
    Guid departurePortId,
    Guid arrivalPortId)
    : Exception(
        $"Route '{routeId}' does not start at port '{departurePortId}' and end at port "
        + $"'{arrivalPortId}'.")
{
    internal Guid RouteId { get; } = routeId;

    internal Guid DeparturePortId { get; } = departurePortId;

    internal Guid ArrivalPortId { get; } = arrivalPortId;
}
