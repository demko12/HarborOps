using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetExpiringDocumentsQuery(DbContext dbContext, IDateTimeProvider dateTimeProvider)
    : IGetExpiringDocumentsQuery
{
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

        var documents = dbContext.Set<Document>().AsNoTracking();

        if (request.From is { } from)
        {
            documents = documents.Where(document => document.ExpiryDate >= from);
        }

        if (request.To is { } to)
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
}
