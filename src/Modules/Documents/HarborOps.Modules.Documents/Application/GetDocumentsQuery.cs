using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentsQuery(DbContext dbContext)
    : IGetDocumentsQuery
{
    public async Task<GetDocumentsResponse> GetDocumentsAsync(
        GetDocumentsRequest request,
        CancellationToken cancellationToken,
        bool restrictToReadOnlyVisible = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetDocumentsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetDocumentsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetDocumentsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetDocumentsValidator.DefaultPageSize;

        var documents = dbContext.Set<Document>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            documents = documents.Where(document => document.VesselId == vesselId);
        }

        if (restrictToReadOnlyVisible)
        {
            documents = documents.Where(document =>
                document.ReviewStatus == Document.ApprovedReviewStatus &&
                (document.Status == Document.ActiveStatus || document.Status == Document.SupersededStatus));
        }

        var rows = await documents
            .Select(document => new
            {
                document.Id,
                document.VesselId,
                document.Number,
                document.ExpiryDate,
                document.Status,
                document.ReviewStatus,
                DocumentTypeName = document.DocumentType.Name,
            })
            .ToListAsync(cancellationToken);

        var filteredRows = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim().ToUpperInvariant();
            filteredRows = filteredRows.Where(
                row => row.Number.Trim().ToUpperInvariant().Contains(normalizedSearch));
        }

        var filteredList = filteredRows.ToList();
        var totalCount = filteredList.Count;

        var items = filteredList
            .OrderBy(row => row.Number)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new DocumentListItem(
                row.Id,
                row.VesselId,
                row.Number,
                row.DocumentTypeName,
                row.Status,
                row.ReviewStatus,
                row.ExpiryDate))
            .ToList();

        return new GetDocumentsResponse(items, pageNumber, pageSize, totalCount);
    }
}
