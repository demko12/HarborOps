using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentTypesQuery(DbContext dbContext)
    : IGetDocumentTypesQuery
{
    public async Task<GetDocumentTypesResponse> GetDocumentTypesAsync(
        GetDocumentTypesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetDocumentTypesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetDocumentTypesValidationException(errors);
        }

        var pageSize = request.PageSize ?? GetDocumentTypesValidator.DefaultPageSize;

        var documentTypes = dbContext.Set<DocumentType>();

        // Projected and filtered in memory rather than via a translated
        // Where(x => x.Name.ToUpper().Contains(...)), so search filtering doesn't depend on
        // SQL Server vs. the in-memory provider's collation/case-sensitivity differences.
        var rows = await documentTypes
            .AsNoTracking()
            .Select(documentType => new
            {
                documentType.Id,
                documentType.Name,
                documentType.MandatoryValidityPeriod,
                documentType.IssuingAuthority,
                documentType.RequiresSurvey,
            })
            .ToListAsync(cancellationToken);

        var filteredRows = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim().ToUpperInvariant();
            filteredRows = filteredRows.Where(
                row => row.Name.Trim().ToUpperInvariant().Contains(normalizedSearch));
        }

        var filteredList = filteredRows.ToList();
        var totalCount = filteredList.Count;

        var items = filteredList
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Take(pageSize)
            .Select(row => new DocumentTypeListItem(
                row.Id,
                row.Name,
                row.MandatoryValidityPeriod,
                row.IssuingAuthority,
                row.RequiresSurvey))
            .ToList();

        return new GetDocumentTypesResponse(items, pageSize, totalCount);
    }
}
