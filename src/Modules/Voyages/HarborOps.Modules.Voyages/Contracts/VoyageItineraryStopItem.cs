namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageItineraryStopItem(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string PortCountry,
    string? UnLocode,
    Guid? BerthId,
    string? BerthName,
    DateTime Eta,
    DateTime Etd,
    DateTime? Ata,
    DateTime? Atd,
    string Status,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? ClearanceStatus = null,
    int ClearanceRequiredItemCount = 0,
    int ClearanceRequiredResolvedCount = 0,
    int ClearanceOverdueRequiredItemCount = 0);
