using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class ReportsDocumentsExpiryQuery(DbContext dbContext) : IReportsDocumentsExpiryQuery
{
    internal const int MaxPageSize = 200;

    public async Task<ReportsDocumentsExpiryResult> SearchAsync(
        ReportsDocumentsExpiryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var documents = dbContext.Set<Document>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            documents = documents.Where(document => document.VesselId == vesselId);
        }

        if (request.DocumentTypeId is { } documentTypeId)
        {
            documents = documents.Where(document => document.DocumentTypeId == documentTypeId);
        }

        if (request.From is { } from)
        {
            documents = documents.Where(document => document.ExpiryDate >= from);
        }

        if (request.To is { } to)
        {
            documents = documents.Where(document => document.ExpiryDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();

            documents = string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)
                ? documents.Where(document => document.ExpiryDate < request.AsOf)
                : documents.Where(document => document.Status == status && document.ExpiryDate >= request.AsOf);
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
                document.DocumentTypeId,
                DocumentTypeName = document.DocumentType.Name,
                document.Number,
                document.IssueDate,
                document.ExpiryDate,
                document.IssuingAuthority,
                document.Status,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ReportsDocumentsExpiryRow(
                row.Id,
                row.VesselId,
                row.DocumentTypeId,
                row.DocumentTypeName,
                row.Number,
                row.IssueDate,
                row.ExpiryDate,
                row.IssuingAuthority,
                row.Status,
                row.ExpiryDate.DayNumber - request.AsOf.DayNumber,
                row.ExpiryDate < request.AsOf))
            .ToList();

        return new ReportsDocumentsExpiryResult(items, pageNumber, pageSize, totalCount);
    }
}
