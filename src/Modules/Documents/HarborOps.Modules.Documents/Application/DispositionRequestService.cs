using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class DispositionRequestService(DbContext dbContext, TimeProvider timeProvider, DocumentDistributionInvalidationService? distributionInvalidation = null)
{
    public async Task<DispositionRequestResponse> CreateAsync(
        Guid documentId,
        CreateDispositionRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await dbContext.Set<Document>()
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Id, d.DocumentTypeId, d.Status, d.SupersededAtUtc, d.RevokedAtUtc, d.ExpiryDate })
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(documentId);
        }

        var hasActiveHold = await dbContext.Set<LegalHold>()
            .AsNoTracking()
            .AnyAsync(hold => hold.DocumentId == documentId && hold.IsActive, cancellationToken);

        if (hasActiveHold)
        {
            throw new RetentionConflictException("An active legal hold blocks disposition for this document.");
        }

        var retentionPolicy = await dbContext.Set<RetentionPolicy>()
            .AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.DocumentTypeId == document.DocumentTypeId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var triggerAtUtc = RetentionLifecycleCalculator.ComputeTriggerAtUtc(
            document.Status, document.SupersededAtUtc, document.RevokedAtUtc, document.ExpiryDate, nowUtc);

        if (retentionPolicy is null || triggerAtUtc is null)
        {
            throw new RetentionConflictException("This document is not yet due for disposition.");
        }

        var dueAtUtc = RetentionLifecycleCalculator.ComputeDueAtUtc(triggerAtUtc.Value, retentionPolicy.RetentionPeriodDays);
        if (dueAtUtc > nowUtc)
        {
            throw new RetentionConflictException("This document is not yet due for disposition.");
        }

        var state = await dbContext.Set<DocumentRetentionState>().SingleOrDefaultAsync(s => s.DocumentId == documentId, cancellationToken);
        if (state is null)
        {
            state = new DocumentRetentionState(documentId);
            await dbContext.Set<DocumentRetentionState>().AddAsync(state, cancellationToken);
        }

        var dispositionRequest = DispositionRequest.Create(
            documentId,
            actorId,
            request.Reason!,
            nowUtc,
            retentionPolicy.RetentionPeriodDays,
            triggerAtUtc.Value);

        state.MoveToPendingDisposition(nowUtc);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(documentId, "Document entered disposition.", cancellationToken);

        await dbContext.Set<DispositionRequest>().AddAsync(dispositionRequest, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DispositionRequestResponse(dispositionRequest.Id, dispositionRequest.Status);
    }

    public async Task<DispositionRequestResponse> ApproveAsync(
        Guid requestId,
        ApproveDispositionRequestRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispositionRequest = await dbContext.Set<DispositionRequest>().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new DispositionRequestNotFoundException(requestId);

        var state = await dbContext.Set<DocumentRetentionState>().SingleOrDefaultAsync(s => s.DocumentId == dispositionRequest.DocumentId, cancellationToken)
            ?? throw new DocumentNotFoundException(dispositionRequest.DocumentId);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        dispositionRequest.Approve(actorId, request.DecisionReason, nowUtc);
        state.MarkDisposed(nowUtc);
        if (distributionInvalidation is not null) await distributionInvalidation.ObsoletePendingAsync(dispositionRequest.DocumentId, "Document was disposed.", cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DispositionRequestResponse(dispositionRequest.Id, dispositionRequest.Status);
    }

    public async Task<DispositionRequestResponse> RejectAsync(
        Guid requestId,
        RejectDispositionRequestRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispositionRequest = await dbContext.Set<DispositionRequest>().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new DispositionRequestNotFoundException(requestId);

        var state = await dbContext.Set<DocumentRetentionState>().SingleOrDefaultAsync(s => s.DocumentId == dispositionRequest.DocumentId, cancellationToken)
            ?? throw new DocumentNotFoundException(dispositionRequest.DocumentId);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        dispositionRequest.Reject(actorId, request.DecisionReason!, nowUtc);
        state.RevertPendingDisposition(nowUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DispositionRequestResponse(dispositionRequest.Id, dispositionRequest.Status);
    }
}
