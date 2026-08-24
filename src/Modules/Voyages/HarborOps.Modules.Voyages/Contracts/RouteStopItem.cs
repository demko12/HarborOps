namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteStopItem(
    Guid PortId,
    string PortName,
    string PortCountry,
    string? UnLocode,
    int StopOrder,
    decimal DistanceFromPrevious,
    int EstimatedDurationFromPrevious);
