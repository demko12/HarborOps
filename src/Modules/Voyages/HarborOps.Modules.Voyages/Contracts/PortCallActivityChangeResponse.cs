namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallActivityChangeResponse(
    Guid ActivityId,
    string Status,
    int Revision,
    Guid Version,
    Guid ActorUserId,
    DateTime ChangedAtUtc);
