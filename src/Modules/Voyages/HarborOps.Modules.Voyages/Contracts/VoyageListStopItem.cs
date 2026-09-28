namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageListStopItem(
    int StopOrder,
    string PortName,
    string Status,
    decimal? Latitude,
    decimal? Longitude);
