using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class DocumentLifecycleService(DbContext dbContext, TimeProvider timeProvider, DocumentDistributionInvalidationService? distributionInvalidation = null)
{
    public async Task CorrectMetadataAsync(Guid documentId, CorrectDocumentMetadataRequest request, CancellationToken cancellationToken)
    {
        var metadata = Validate(request.DocumentTypeId, request.Number, request.IssueDate, request.ExpiryDate, request.IssuingAuthority);
        var document = await dbContext.Set<Document>().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        await EnsureTypeExistsAsync(metadata.DocumentTypeId, cancellationToken);
        await EnsureNoActiveDuplicateAsync(document.VesselId, metadata.DocumentTypeId, metadata.Number, documentId, cancellationToken);
        document.CorrectMetadata(metadata.DocumentTypeId, metadata.Number, metadata.IssueDate, metadata.ExpiryDate, metadata.IssuingAuthority);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document metadata was corrected.", cancellationToken);
        if (string.Equals(document.ReviewStatus, Document.RejectedReviewStatus, StringComparison.Ordinal))
        {
            document.ReturnToDraftAfterCorrection();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> RenewAsync(Guid documentId, RenewDocumentRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        var metadata = Validate(request.DocumentTypeId, request.Number, request.IssueDate, request.ExpiryDate, request.IssuingAuthority);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var predecessor = await dbContext.Set<Document>().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        if (!string.Equals(predecessor.Status, Document.ActiveStatus, StringComparison.Ordinal))
        {
            throw new DocumentLifecycleConflictException("Only an active document can be renewed.");
        }
        await EnsureTypeExistsAsync(metadata.DocumentTypeId, cancellationToken);
        await EnsureNoActiveDuplicateAsync(predecessor.VesselId, metadata.DocumentTypeId, metadata.Number, null, cancellationToken);
        var successor = new Document(predecessor.VesselId, metadata.DocumentTypeId, metadata.Number, metadata.IssueDate, metadata.ExpiryDate, metadata.IssuingAuthority, Document.ActiveStatus);
        successor.LinkToPredecessor(predecessor.Id);
        predecessor.Supersede(actorId, timeProvider.GetUtcNow().UtcDateTime);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document was renewed and superseded.", cancellationToken);
        await dbContext.Set<Document>().AddAsync(successor, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return successor.Id;
    }

    public async Task<IReadOnlyList<DocumentRenewalHistoryItem>> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var documents = dbContext.Set<Document>().AsNoTracking();
        var all = await documents.Select(d => new DocumentRenewalHistoryItem(d.Id, d.PredecessorDocumentId, d.Number, d.Status, d.ExpiryDate, d.SupersededAtUtc, d.SupersededByUserId)).ToListAsync(cancellationToken);
        var selected = all.SingleOrDefault(d => d.DocumentId == documentId);
        if (selected is null) throw new DocumentNotFoundException(documentId);
        var relatedIds = new HashSet<Guid> { documentId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var item in all.Where(item => (item.PredecessorDocumentId is { } predecessor && relatedIds.Contains(predecessor)) || relatedIds.Contains(item.DocumentId)))
            {
                changed |= relatedIds.Add(item.DocumentId);
                if (item.PredecessorDocumentId is { } predecessor) changed |= relatedIds.Add(predecessor);
            }
        }
        var chain = all.Where(item => relatedIds.Contains(item.DocumentId))
            .OrderByDescending(d => d.SupersededAtUtc).ThenByDescending(d => d.DocumentId)
            .ToList();
        return chain;
    }

    public async Task SubmitForReviewAsync(Guid documentId, Guid actorId, CancellationToken cancellationToken)
    {
        var document = await dbContext.Set<Document>().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        var utcNow = timeProvider.GetUtcNow();
        document.SubmitForReview(actorId, utcNow);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document review status changed.", cancellationToken);

        await dbContext.Set<DocumentReviewEvent>().AddAsync(
            new DocumentReviewEvent(documentId, DocumentReviewEvent.SubmittedAction, actorId, utcNow, notes: null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReviewAsync(Guid documentId, Guid reviewerId, string decision, string? notes, CancellationToken cancellationToken)
    {
        var document = await dbContext.Set<Document>().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        var submitterId = await dbContext.Set<DocumentReviewEvent>()
            .AsNoTracking()
            .Where(e => e.DocumentId == documentId && e.Action == DocumentReviewEvent.SubmittedAction)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Select(e => (Guid?)e.ActorUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (submitterId is { } submitter && submitter == reviewerId)
        {
            throw new DocumentLifecycleConflictException("The reviewer must be different from the submitter.");
        }

        var utcNow = timeProvider.GetUtcNow();

        if (string.Equals(decision, Document.ApprovedReviewStatus, StringComparison.Ordinal))
        {
            document.Approve(reviewerId, utcNow);
            await dbContext.Set<DocumentReviewEvent>().AddAsync(
                new DocumentReviewEvent(documentId, DocumentReviewEvent.ApprovedAction, reviewerId, utcNow, notes),
                cancellationToken);
        }
        else if (string.Equals(decision, Document.RejectedReviewStatus, StringComparison.Ordinal))
        {
            document.Reject(reviewerId, notes!, utcNow);
            await dbContext.Set<DocumentReviewEvent>().AddAsync(
                new DocumentReviewEvent(documentId, DocumentReviewEvent.RejectedAction, reviewerId, utcNow, notes),
                cancellationToken);
        }

        else
        {
            throw new ArgumentException($"'{decision}' is not a recognized review decision.", nameof(decision));
        }

        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document review status changed.", cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid documentId, Guid actorId, string reason, CancellationToken cancellationToken)
    {
        var document = await dbContext.Set<Document>().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        var utcNow = timeProvider.GetUtcNow();
        document.Revoke(actorId, reason, utcNow);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document was revoked.", cancellationToken);

        await dbContext.Set<DocumentReviewEvent>().AddAsync(
            new DocumentReviewEvent(documentId, DocumentReviewEvent.RevokedAction, actorId, utcNow, reason),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentReviewEventResponse>> GetReviewHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var documentExists = await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            throw new DocumentNotFoundException(documentId);
        }

        return await dbContext.Set<DocumentReviewEvent>()
            .AsNoTracking()
            .Where(e => e.DocumentId == documentId)
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Select(e => new DocumentReviewEventResponse(e.Id, e.DocumentId, e.Action, e.ActorUserId, e.OccurredAtUtc, e.Notes))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureTypeExistsAsync(int documentTypeId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == documentTypeId, cancellationToken))
            throw new DocumentTypeNotFoundException(documentTypeId);
    }
    private async Task EnsureNoActiveDuplicateAsync(Guid vesselId, int typeId, string number, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.VesselId == vesselId && d.DocumentTypeId == typeId && d.Number == number && d.Status == Document.ActiveStatus && d.Id != excludedId, cancellationToken))
            throw new DocumentLifecycleConflictException("An active document with this vessel, type, and number already exists.");
    }
    private static Metadata Validate(int? typeId, string? number, DateOnly? issue, DateOnly? expiry, string? authority)
    {
        var errors = new Dictionary<string, string[]>();
        if (typeId is null or <= 0) errors["documentTypeId"] = ["Document type is required."];
        if (string.IsNullOrWhiteSpace(number)) errors["number"] = ["Document number is required."];
        else if (number.Trim().Length > Document.NumberMaxLength) errors["number"] = ["Document number is too long."];
        if (issue is null) errors["issueDate"] = ["Issue date is required."];
        if (expiry is null) errors["expiryDate"] = ["Expiry date is required."];
        else if (issue is not null && expiry < issue) errors["expiryDate"] = ["Expiry date must not be earlier than the issue date."];
        if (string.IsNullOrWhiteSpace(authority)) errors["issuingAuthority"] = ["Issuing authority is required."];
        else if (authority.Trim().Length > Document.IssuingAuthorityMaxLength) errors["issuingAuthority"] = ["Issuing authority is too long."];
        if (errors.Count > 0) throw new DocumentLifecycleValidationException(errors);
        return new Metadata(typeId!.Value, number!.Trim(), issue!.Value, expiry!.Value, authority!.Trim());
    }
    private sealed record Metadata(int DocumentTypeId, string Number, DateOnly IssueDate, DateOnly ExpiryDate, string IssuingAuthority);
}
