namespace HarborOps.Modules.Voyages.Contracts;

public sealed record AddPortCallClearanceItemResponse(
    Guid ItemId,
    Guid ClearanceId,
    string Status,
    Guid AddedByUserId,
    DateTime AddedAtUtc);
