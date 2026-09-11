namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceResponse(
    Guid VoyageId,
    Guid StopId,
    Guid ClearanceId,
    string Status,
    Guid? CompletedByUserId,
    DateTime? CompletedAtUtc,
    DateTime EvaluatedAtUtc,
    PortCallClearanceProgress Progress,
    IReadOnlyList<PortCallClearanceItemModel> Items,
    IReadOnlyList<PortCallClearanceEventModel> History);
