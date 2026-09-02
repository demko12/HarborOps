namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReviewEventResponse(
    Guid Id,
    Guid DocumentId,
    string Action,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? Notes);
