namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceProgress(
    int TotalItemCount,
    int RequiredItemCount,
    int RequiredResolvedCount,
    int RequiredPendingCount,
    int OverdueRequiredItemCount,
    int OptionalItemCount,
    int OptionalPendingCount,
    int ClearedCount,
    int WaivedCount,
    bool CanComplete);
