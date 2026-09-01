using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentsQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetDocumentsQuery
{
    public async Task<GetDocumentsResponse> GetDocumentsAsync(
        GetDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
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

        var rows = await documents
            .Select(document => new
            {
                document.Id,
                document.VesselId,
                document.Number,
                document.ExpiryDate,
                Status = document.Status == Document.ActiveStatus && document.ExpiryDate < today ? "Expired" : document.Status,
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
                row.ExpiryDate))
            .ToList();

        return new GetDocumentsResponse(items, pageNumber, pageSize, totalCount);
    }
}
