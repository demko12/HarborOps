using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

public sealed class ComplianceExposureReportService(IReportsComplianceExposureQuery complianceExposureQuery)
{
    public async Task<ComplianceExposureReportPage> GetPageAsync(
        DateOnly? asOf,
        Guid? vesselId,
        string? category,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var errors = ComplianceExposureReportValidator.Validate(asOf, pageNumber, pageSize, category);

        if (errors.Count > 0)
        {
            throw new ComplianceExposureReportValidationException(errors);
        }

        var result = await complianceExposureQuery.SearchAsync(
            new ReportsComplianceExposureRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                Category = category,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
            cancellationToken);

        return new ComplianceExposureReportPage(
            asOf.Value,
            result.Items.Select(Compose).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<(DateOnly AsOf, IReadOnlyList<ComplianceExposureReportRow> Rows)> GetExportRowsAsync(
        DateOnly? asOf,
        Guid? vesselId,
        string? category,
        CancellationToken cancellationToken)
    {
        var errors = ComplianceExposureReportValidator.Validate(asOf, pageNumber: 1, pageSize: 1, category);

        if (errors.Count > 0)
        {
            throw new ComplianceExposureReportValidationException(errors);
        }

        var result = await complianceExposureQuery.SearchAsync(
            new ReportsComplianceExposureRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                Category = category,
                PageNumber = 1,
                PageSize = ComplianceExposureReportValidator.MaxExportRows,
            },
            cancellationToken);

        return (asOf.Value, result.Items.Select(Compose).ToArray());
    }

    private static ComplianceExposureReportRow Compose(ReportsComplianceExposureRow row) => new(
        row.ItemType,
        row.VesselId,
        row.Reference,
        row.Description,
        row.RelevantDate,
        row.Status,
        row.Severity);
}
