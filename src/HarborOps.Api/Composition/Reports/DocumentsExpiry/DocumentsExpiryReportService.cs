using HarborOps.Modules.Documents.Contracts;

namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

public sealed class DocumentsExpiryReportService(IReportsDocumentsExpiryQuery documentsExpiryQuery)
{
    public async Task<DocumentsExpiryReportPage> GetPageAsync(
        DateOnly? asOf,
        Guid? vesselId,
        int? documentTypeId,
        DateOnly? from,
        DateOnly? to,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var errors = DocumentsExpiryReportValidator.Validate(asOf, pageNumber, pageSize, from, to);

        if (errors.Count > 0)
        {
            throw new DocumentsExpiryReportValidationException(errors);
        }

        var result = await documentsExpiryQuery.SearchAsync(
            new ReportsDocumentsExpiryRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                DocumentTypeId = documentTypeId,
                From = from,
                To = to,
                Status = status,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
            cancellationToken);

        return new DocumentsExpiryReportPage(
            asOf.Value,
            result.Items.Select(Compose).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<(DateOnly AsOf, IReadOnlyList<DocumentsExpiryReportRow> Rows)> GetExportRowsAsync(
        DateOnly? asOf,
        Guid? vesselId,
        int? documentTypeId,
        DateOnly? from,
        DateOnly? to,
        string? status,
        CancellationToken cancellationToken)
    {
        var errors = DocumentsExpiryReportValidator.Validate(asOf, pageNumber: 1, pageSize: 1, from, to);

        if (errors.Count > 0)
        {
            throw new DocumentsExpiryReportValidationException(errors);
        }

        var result = await documentsExpiryQuery.SearchAsync(
            new ReportsDocumentsExpiryRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                DocumentTypeId = documentTypeId,
                From = from,
                To = to,
                Status = status,
                PageNumber = 1,
                PageSize = DocumentsExpiryReportValidator.MaxExportRows,
            },
            cancellationToken);

        return (asOf.Value, result.Items.Select(Compose).ToArray());
    }

    private static DocumentsExpiryReportRow Compose(ReportsDocumentsExpiryRow row) => new(
        row.DocumentId,
        row.VesselId,
        row.DocumentTypeId,
        row.DocumentTypeName,
        row.Number,
        row.IssueDate,
        row.ExpiryDate,
        row.IssuingAuthority,
        row.Status,
        row.DaysUntilExpiry,
        row.IsExpired);
}
