namespace HarborOps.Modules.Documents.Contracts;

public sealed record IssueDocumentDistributionRequest(DateOnly? DueDate, string? Instruction, IReadOnlyList<Guid>? RecipientUserIds);
public sealed record CancelDocumentDistributionRequest(string? Reason);
public sealed record DocumentDistributionRecipientResponse(Guid RecipientUserId, string State, bool IsOverdue, DateTime? AcknowledgedAtUtc, DateTime? StateChangedAtUtc, string? StateReason);
public sealed record DocumentDistributionIssueResponse(Guid Id, Guid DocumentId, string DocumentNumber, string DocumentType, DateOnly DocumentExpiryDate, Guid FileRevisionId, DateOnly DueDate, string? Instruction, Guid IssuedByUserId, DateTime IssuedAtUtc, bool IsCancelled, string? CancellationReason, IReadOnlyList<DocumentDistributionRecipientResponse> Recipients);
public sealed record DocumentDistributionInboxItem(Guid IssueId, Guid DocumentId, string DocumentNumber, string DocumentType, DateOnly DocumentExpiryDate, DateOnly DueDate, string? Instruction, string State, bool IsOverdue, DateTime IssuedAtUtc, DateTime? AcknowledgedAtUtc, string? StateReason);
public sealed record GetDocumentDistributionInboxRequest(string? State, bool? Overdue, int? PageNumber, int? PageSize);
public sealed record GetDocumentDistributionInboxResponse(IReadOnlyList<DocumentDistributionInboxItem> Items, int PageNumber, int PageSize, int TotalCount);

/// <summary>Implemented by API composition; Documents never reads Identity tables.</summary>
public interface IDocumentDistributionRecipientLookup
{
    Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> LookupAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> SearchAsync(string? search, int take, CancellationToken cancellationToken);
}

public sealed record DocumentDistributionRecipientIdentity(Guid UserId, string Email, bool IsActive, bool CanReadDocuments);
