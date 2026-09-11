namespace HarborOps.Modules.Documents.Contracts;

public sealed record RetentionQueueItem(
    Guid DocumentId,
    int DocumentTypeId,
    DateTime LifecycleTriggerAtUtc,
    DateTime RetentionDueAtUtc,
    string RetentionState,
    IReadOnlyList<RetentionQueueActiveHold> ActiveHolds,
    RetentionQueuePendingDisposition? PendingDisposition);
