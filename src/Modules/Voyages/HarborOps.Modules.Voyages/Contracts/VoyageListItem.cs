namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageListItem(
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
