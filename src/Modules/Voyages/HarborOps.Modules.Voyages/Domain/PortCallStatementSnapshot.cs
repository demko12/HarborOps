namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallStatementSnapshot(
    string VoyageNumber,
    int StopOrder,
    string PortName,
    string? PortUnLocode,
    string? PortTimeZone,
    string? BerthName,
    DateTime ActualArrivalUtc,
    DateTime ActualDepartureUtc,
    string SourceVersion,
    IReadOnlyList<PortCallLogEntry> Entries);
