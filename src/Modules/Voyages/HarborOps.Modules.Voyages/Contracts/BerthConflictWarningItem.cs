namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthConflictWarningItem(
    Guid StopId,
    int StopOrder,
    Guid BerthId,
    string BerthName,
    DateTime ConflictStart,
    DateTime ConflictEnd);
