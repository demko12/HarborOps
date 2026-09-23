namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallTimelineResponse(
    Guid VoyageId,
    Guid StopId,
    string StopStatus,
    string PortName,
    string? PortTimeZone,
    string? BerthName,
    DateTime? ActualArrivalUtc,
    DateTime? ActualDepartureUtc,
    bool CanRecordActivities,
    PortCallStatementSummary? Statement,
    IReadOnlyList<PortCallLogEntryItem> Entries,
    IReadOnlyList<PortCallActivityItem> Activities);
