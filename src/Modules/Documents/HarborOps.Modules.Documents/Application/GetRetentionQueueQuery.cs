using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetRetentionQueueQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetRetentionQueueQuery
{
    public async Task<GetRetentionQueueResponse> GetRetentionQueueAsync(
        GetRetentionQueueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetRetentionQueueValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetRetentionQueueValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetRetentionQueueValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetRetentionQueueValidator.DefaultPageSize;

        // Captured once so the query is a stable, parameterized SQL statement rather than
        // re-evaluating "now" per row.
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);

        // Inner join to RetentionPolicy excludes documents with no matching policy (no due
        // date can be computed for them). Left join to DocumentRetentionState defaults a
        // missing row to Active via the coalesce in the projection below, rather than a
        // post-fetch patch. The WHERE clause below expresses eligibility (which of the three
        // lifecycle-trigger cases applies) purely via DateOnly/DateTime column comparisons -
        // no DateOnly-to-DateTime column conversion is needed for it, so it's unambiguously
        // SQL-translatable.
        var eligible =
            from document in dbContext.Set<Document>().AsNoTracking()
            join policy in dbContext.Set<RetentionPolicy>().AsNoTracking()
                on document.DocumentTypeId equals policy.DocumentTypeId
            join state in dbContext.Set<DocumentRetentionState>().AsNoTracking()
                on document.Id equals state.DocumentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            where
                (document.Status == Document.SupersededStatus && document.SupersededAtUtc != null) ||
                (document.Status == Document.RevokedStatus && document.RevokedAtUtc != null) ||
                (document.Status == Document.ActiveStatus && document.ExpiryDate < today)
            select new
            {
                document.Id,
                document.DocumentTypeId,
                RetentionState = state != null ? state.State : DocumentRetentionState.ActiveState,
                // EF Core translates DateOnly.ToDateTime(TimeOnly) on SQL Server (native
                // date/time member translation added in EF Core 8), so this stays one
                // server-side expression alongside the two DateTime columns it's unified with.
                TriggerAtUtc = document.Status == Document.SupersededStatus
                    ? document.SupersededAtUtc!.Value
                    : document.Status == Document.RevokedStatus
                        ? document.RevokedAtUtc!.Value
                        : document.ExpiryDate.ToDateTime(TimeOnly.MinValue),
                policy.RetentionPeriodDays,
            };

        var withDueDate = eligible.Select(candidate => new
        {
            candidate.Id,
            candidate.DocumentTypeId,
            candidate.RetentionState,
            candidate.TriggerAtUtc,
            DueAtUtc = candidate.TriggerAtUtc.AddDays(candidate.RetentionPeriodDays),
        });

        var filtered = withDueDate;

        if (!string.IsNullOrWhiteSpace(request.State))
        {
            var state = request.State;
            filtered = filtered.Where(candidate => candidate.RetentionState == state);
        }

        if (request.DueBefore is { } dueBefore)
        {
            // dueBefore is a request-local DateOnly value (not a column), so converting it
            // once here is a client-side constant, not a per-row column cast - the same
            // pattern GetVoyagesQuery/ReportsMaintenanceBacklogQuery already use for their
            // DateOnly request filters against DateTime columns.
            var dueBeforeAtUtc = dueBefore.ToDateTime(TimeOnly.MinValue);
            filtered = filtered.Where(candidate => candidate.DueAtUtc < dueBeforeAtUtc);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var page = await filtered
            .OrderBy(candidate => candidate.DueAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // The active-holds list and pending-disposition summary are per-item display data,
        // not filter criteria, so they're fetched in one small query each, scoped to just
        // this page's document ids - bounded by page size, not the full table.
        var pageDocumentIds = page.Select(candidate => candidate.Id).ToList();

        var activeHoldsByDocument = (await dbContext.Set<LegalHold>()
            .AsNoTracking()
            .Where(hold => hold.IsActive && pageDocumentIds.Contains(hold.DocumentId))
            .Select(hold => new { hold.DocumentId, hold.Id, hold.Reason, hold.PlacedAtUtc })
            .ToListAsync(cancellationToken))
            .GroupBy(hold => hold.DocumentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RetentionQueueActiveHold>)group
                    .Select(hold => new RetentionQueueActiveHold(hold.Id, hold.Reason, hold.PlacedAtUtc))
                    .ToList());

        var pendingDispositionsByDocument = (await dbContext.Set<DispositionRequest>()
            .AsNoTracking()
            .Where(dispositionRequest =>
                dispositionRequest.Status == DispositionRequest.PendingStatus &&
                pageDocumentIds.Contains(dispositionRequest.DocumentId))
            .Select(dispositionRequest => new { dispositionRequest.DocumentId, dispositionRequest.Id, dispositionRequest.RequestedByUserId, dispositionRequest.RequestedAtUtc })
            .ToListAsync(cancellationToken))
            .ToDictionary(
                item => item.DocumentId,
                item => new RetentionQueuePendingDisposition(item.Id, item.RequestedByUserId, item.RequestedAtUtc));

        var items = page
            .Select(candidate => new RetentionQueueItem(
                candidate.Id,
                candidate.DocumentTypeId,
                candidate.TriggerAtUtc,
                candidate.DueAtUtc,
                candidate.RetentionState,
                activeHoldsByDocument.TryGetValue(candidate.Id, out var holds) ? holds : [],
                pendingDispositionsByDocument.GetValueOrDefault(candidate.Id)))
            .ToList();

        return new GetRetentionQueueResponse(items, pageNumber, pageSize, totalCount);
    }
}
