namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceItemDecisionResponse(
    Guid ItemId,
    string Status,
    Guid DecidedByUserId,
    DateTime DecidedAtUtc);
