using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetExpiringDocumentsQuery(DbContext dbContext, IDateTimeProvider dateTimeProvider)
    : IGetExpiringDocumentsQuery
{
    internal const int DueSoonDays = 30;
    internal const int MonthsAhead = 12;

    public async Task<GetExpiringDocumentsResponse> GetExpiringDocumentsAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetExpiringDocumentsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetExpiringDocumentsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetExpiringDocumentsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetExpiringDocumentsValidator.DefaultPageSize;

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        var documents = Filter(request, restrictToReadOnlyVisible, applyDateRange: true);

        var totalCount = await documents.CountAsync(cancellationToken);

        var rows = await documents
            .OrderBy(document => document.ExpiryDate)
            .ThenBy(document => document.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(document => new
            {
                document.Id,
                document.VesselId,
                document.Number,
                document.IssueDate,
                document.ExpiryDate,
                document.IssuingAuthority,
                document.Status,
                document.ReviewStatus,
                document.DocumentTypeId,
                DocumentTypeName = document.DocumentType.Name,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ExpiringDocumentListItem(
                row.Id,
                row.VesselId,
                row.Number,
                row.IssueDate,
                row.ExpiryDate,
                row.IssuingAuthority,
                row.Status,
                row.ReviewStatus,
                row.DocumentTypeId,
                row.DocumentTypeName,
                row.ExpiryDate.DayNumber - today.DayNumber))
            .ToList();

        return new GetExpiringDocumentsResponse(items, pageNumber, pageSize, totalCount);
    }

    public async Task<GetExpiringDocumentsSummaryResponse> GetSummaryAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetExpiringDocumentsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetExpiringDocumentsValidationException(errors);
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var dueLimit = today.AddDays(DueSoonDays);

        var counts = await Filter(request, restrictToReadOnlyVisible, applyDateRange: true)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Expired = group.Count(document => document.ExpiryDate < today),
                DueSoon = group.Count(document => document.ExpiryDate >= today && document.ExpiryDate <= dueLimit),
                PendingReview = group.Count(document => document.ReviewStatus == Document.PendingReviewReviewStatus),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var firstMonth = new DateOnly(today.Year, today.Month, 1);
        var monthsEnd = firstMonth.AddMonths(MonthsAhead);
        var monthCounts = await Filter(request, restrictToReadOnlyVisible, applyDateRange: false)
            .Where(document => document.ExpiryDate >= today && document.ExpiryDate < monthsEnd)
            .GroupBy(document => new { document.ExpiryDate.Year, document.ExpiryDate.Month })
            .Select(group => new { group.Key.Year, group.Key.Month, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var months = Enumerable.Range(0, MonthsAhead)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => new ExpiringDocumentsMonthCount(
                month.Year,
                month.Month,
                monthCounts.SingleOrDefault(row => row.Year == month.Year && row.Month == month.Month)?.Count ?? 0))
            .ToList();

        return new GetExpiringDocumentsSummaryResponse(
            counts?.Total ?? 0,
            counts?.Expired ?? 0,
            counts?.DueSoon ?? 0,
            counts?.PendingReview ?? 0,
            months);
    }

    private IQueryable<Document> Filter(
        GetExpiringDocumentsRequest request,
        bool restrictToReadOnlyVisible,
        bool applyDateRange)
    {
        var documents = dbContext.Set<Document>().AsNoTracking();

        if (applyDateRange && request.From is { } from)
        {
            documents = documents.Where(document => document.ExpiryDate >= from);
        }

        if (applyDateRange && request.To is { } to)
        {
            documents = documents.Where(document => document.ExpiryDate <= to);
        }

        if (request.VesselId is { } vesselId)
        {
            documents = documents.Where(document => document.VesselId == vesselId);
        }

        if (request.ReviewStatus is { } reviewStatus)
        {
            documents = documents.Where(document => document.ReviewStatus == reviewStatus);
        }

        if (restrictToReadOnlyVisible)
        {
            documents = documents.Where(document =>
                document.ReviewStatus == Document.ApprovedReviewStatus &&
                (document.Status == Document.ActiveStatus || document.Status == Document.SupersededStatus));
        }

        return documents;
    }
}
