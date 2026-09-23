namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallActivityDetailsResponse(
    Guid VoyageId,
    Guid StopId,
    PortCallActivityItem Activity,
    IReadOnlyList<PortCallActivityRevisionItem> History);
