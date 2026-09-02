namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageScheduleSummary(
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
