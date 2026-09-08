namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoSafetyReviewEntryListItem(
    Guid CargoSafetyReviewId,
    string FromStatus,
    string ToStatus,
    Guid ActorUserId,
    DateTime OccurredUtc,
    string? Notes);
