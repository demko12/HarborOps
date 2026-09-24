using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class DocumentDistributionService(
    DbContext dbContext,
    IDocumentDistributionRecipientLookup recipientLookup,
    TimeProvider timeProvider)
{
    private const int MaxRecipients = 100;

    public async Task<DocumentDistributionIssueResponse> IssueAsync(Guid documentId, IssueDocumentDistributionRequest request, Guid issuerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recipientIds = ValidateIssue(request);
        var identities = await recipientLookup.LookupAsync(recipientIds, cancellationToken);
        if (identities.Count != recipientIds.Count || identities.Any(x => !x.IsActive || !x.CanReadDocuments))
            throw new DocumentDistributionValidationException(new Dictionary<string, string[]> { ["recipientUserIds"] = ["Every recipient must be active and authorized to read documents."] });

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var document = await dbContext.Set<Document>().Include(x => x.DocumentType).SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        var retention = await dbContext.Set<DocumentRetentionState>().AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == documentId, cancellationToken);
        EnsureIssueable(document, retention?.State, today);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var issue = new DocumentDistributionIssue(document, document.DocumentType.Name, request.DueDate!.Value, Normalize(request.Instruction), issuerId, now);
        await dbContext.Set<DocumentDistributionIssue>().AddAsync(issue, cancellationToken);
        await dbContext.Set<DocumentDistributionRecipient>().AddRangeAsync(recipientIds.Select(x => new DocumentDistributionRecipient(issue.Id, x)), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToIssueResponse(issue, recipientIds.Select(x => new DocumentDistributionRecipientResponse(x, DocumentDistributionRecipient.PendingState, request.DueDate < today, null, null, null)).ToArray());
    }

    public async Task<DocumentDistributionIssueResponse> GetIssueAsync(Guid documentId, Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await dbContext.Set<DocumentDistributionIssue>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == issueId && x.DocumentId == documentId, cancellationToken)
            ?? throw new DocumentDistributionIssueNotFoundException(issueId);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var recipients = await dbContext.Set<DocumentDistributionRecipient>().AsNoTracking().Where(x => x.IssueId == issueId)
            .OrderBy(x => x.RecipientUserId).Select(x => new DocumentDistributionRecipientResponse(x.RecipientUserId, x.State, x.State == DocumentDistributionRecipient.PendingState && issue.DueDate < today, x.AcknowledgedAtUtc, x.StateChangedAtUtc, x.StateReason)).ToListAsync(cancellationToken);
        return ToIssueResponse(issue, recipients);
    }

    public async Task<IReadOnlyList<DocumentDistributionIssueResponse>> GetIssuesAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var issueIds = await dbContext.Set<DocumentDistributionIssue>().AsNoTracking().Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.IssuedAtUtc).Select(x => x.Id).ToListAsync(cancellationToken);
        return await Task.WhenAll(issueIds.Select(x => GetIssueAsync(documentId, x, cancellationToken)));
    }

    public async Task CancelAsync(Guid documentId, Guid issueId, string? reason, Guid actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > DocumentDistributionIssue.ReasonMaxLength)
            throw new DocumentDistributionValidationException(new Dictionary<string, string[]> { ["reason"] = ["A cancellation reason of up to 1000 characters is required."] });
        var issue = await dbContext.Set<DocumentDistributionIssue>().SingleOrDefaultAsync(x => x.Id == issueId && x.DocumentId == documentId, cancellationToken)
            ?? throw new DocumentDistributionIssueNotFoundException(issueId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        issue.Cancel(actorId, reason.Trim(), now);
        var pending = await dbContext.Set<DocumentDistributionRecipient>().Where(x => x.IssueId == issueId && x.State == DocumentDistributionRecipient.PendingState).ToListAsync(cancellationToken);
        foreach (var recipient in pending) recipient.Cancel(now, reason.Trim());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<GetDocumentDistributionInboxResponse> GetInboxAsync(Guid recipientId, GetDocumentDistributionInboxRequest request, CancellationToken cancellationToken)
    {
        var identity = (await recipientLookup.LookupAsync([recipientId], cancellationToken)).SingleOrDefault();
        if (identity is null || !identity.IsActive || !identity.CanReadDocuments) throw new UnauthorizedAccessException();
        var (page, size) = ValidatePage(request.PageNumber, request.PageSize);
        var query = from recipient in dbContext.Set<DocumentDistributionRecipient>().AsNoTracking()
                    join issue in dbContext.Set<DocumentDistributionIssue>().AsNoTracking() on recipient.IssueId equals issue.Id
                    where recipient.RecipientUserId == recipientId
                    select new { recipient, issue };
        if (!string.IsNullOrWhiteSpace(request.State)) query = query.Where(x => x.recipient.State == request.State);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (request.Overdue is not null) query = request.Overdue.Value
            ? query.Where(x => x.recipient.State == DocumentDistributionRecipient.PendingState && x.issue.DueDate < today)
            : query.Where(x => x.recipient.State != DocumentDistributionRecipient.PendingState || x.issue.DueDate >= today);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.recipient.State == DocumentDistributionRecipient.PendingState ? 0 : 1).ThenBy(x => x.issue.DueDate).ThenByDescending(x => x.issue.IssuedAtUtc)
            .Skip((page - 1) * size).Take(size).Select(x => new DocumentDistributionInboxItem(x.issue.Id, x.issue.DocumentId, x.issue.DocumentNumberSnapshot, x.issue.DocumentTypeSnapshot, x.issue.DocumentExpirySnapshot, x.issue.DueDate, x.issue.Instruction, x.recipient.State, x.recipient.State == DocumentDistributionRecipient.PendingState && x.issue.DueDate < today, x.issue.IssuedAtUtc, x.recipient.AcknowledgedAtUtc, x.recipient.StateReason)).ToListAsync(cancellationToken);
        return new GetDocumentDistributionInboxResponse(items, page, size, total);
    }

    public async Task<DocumentDistributionRecipientResponse> AcknowledgeAsync(Guid issueId, Guid recipientId, CancellationToken cancellationToken)
    {
        var identity = (await recipientLookup.LookupAsync([recipientId], cancellationToken)).SingleOrDefault();
        if (identity is null || !identity.IsActive || !identity.CanReadDocuments) throw new UnauthorizedAccessException();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var recipient = await dbContext.Set<DocumentDistributionRecipient>().SingleOrDefaultAsync(x => x.IssueId == issueId && x.RecipientUserId == recipientId, cancellationToken)
            ?? throw new DocumentDistributionIssueNotFoundException(issueId);
        var issue = await dbContext.Set<DocumentDistributionIssue>().SingleOrDefaultAsync(x => x.Id == issueId, cancellationToken)
            ?? throw new DocumentDistributionIssueNotFoundException(issueId);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (recipient.State == DocumentDistributionRecipient.AcknowledgedState)
            return new DocumentDistributionRecipientResponse(recipientId, recipient.State, false, recipient.AcknowledgedAtUtc, recipient.StateChangedAtUtc, recipient.StateReason);
        if (recipient.State != DocumentDistributionRecipient.PendingState) throw new DocumentDistributionConflictException("This acknowledgement is no longer pending.");
        var document = await dbContext.Set<Document>().SingleOrDefaultAsync(x => x.Id == issue.DocumentId, cancellationToken) ?? throw new DocumentNotFoundException(issue.DocumentId);
        var retention = await dbContext.Set<DocumentRetentionState>().AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == document.Id, cancellationToken);
        if (issue.IsCancelled || !MatchesSnapshot(issue, document) || !IsIssueable(document, retention?.State, today))
        {
            recipient.MarkObsolete(timeProvider.GetUtcNow().UtcDateTime, "The issued document content or lifecycle state has changed.");
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DocumentDistributionConflictException("The document changed after it was issued. Acknowledgement requires a fresh issue.");
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        recipient.Acknowledge(recipientId, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DocumentDistributionRecipientResponse(recipientId, recipient.State, false, recipient.AcknowledgedAtUtc, recipient.StateChangedAtUtc, null);
    }

    private static IReadOnlyList<Guid> ValidateIssue(IssueDocumentDistributionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.DueDate is null) errors["dueDate"] = ["A due date is required."];
        if (request.Instruction?.Length > DocumentDistributionIssue.InstructionMaxLength) errors["instruction"] = ["Instruction is too long."];
        var ids = request.RecipientUserIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (ids.Length == 0) errors["recipientUserIds"] = ["At least one recipient is required."];
        if (request.RecipientUserIds is not null && ids.Length != request.RecipientUserIds.Count) errors["recipientUserIds"] = ["Recipients must be distinct valid user IDs."];
        if (ids.Length > MaxRecipients) errors["recipientUserIds"] = ["At most 100 recipients can be issued at once."];
        if (errors.Count > 0) throw new DocumentDistributionValidationException(errors);
        return ids;
    }
    private static (int Page, int Size) ValidatePage(int? pageNumber, int? pageSize)
    {
        var page = pageNumber ?? 1; var size = pageSize ?? 25;
        if (page < 1 || size is < 1 or > 100) throw new DocumentDistributionValidationException(new Dictionary<string, string[]> { ["page"] = ["Page number must be at least 1 and page size must be between 1 and 100."] });
        return (page, size);
    }
    private static void EnsureIssueable(Document document, string? retentionState, DateOnly today)
    {
        if (!IsIssueable(document, retentionState, today)) throw new DocumentDistributionConflictException("Only active, approved, unexpired documents with a downloadable file and active retention state can be issued.");
    }
    private static bool IsIssueable(Document document, string? retentionState, DateOnly today) => document.Status == Document.ActiveStatus && document.ReviewStatus == Document.ApprovedReviewStatus && document.ExpiryDate >= today && document.HasFile && document.FileRevisionId is not null && (retentionState is null || retentionState == DocumentRetentionState.ActiveState);
    private static bool MatchesSnapshot(DocumentDistributionIssue issue, Document document) => issue.FileRevisionId == document.FileRevisionId && issue.DocumentConcurrencyToken == document.ConcurrencyToken && issue.DocumentNumberSnapshot == document.Number && issue.DocumentExpirySnapshot == document.ExpiryDate;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DocumentDistributionIssueResponse ToIssueResponse(DocumentDistributionIssue issue, IReadOnlyList<DocumentDistributionRecipientResponse> recipients) => new(issue.Id, issue.DocumentId, issue.DocumentNumberSnapshot, issue.DocumentTypeSnapshot, issue.DocumentExpirySnapshot, issue.FileRevisionId, issue.DueDate, issue.Instruction, issue.IssuedByUserId, issue.IssuedAtUtc, issue.IsCancelled, issue.CancellationReason, recipients);
}
