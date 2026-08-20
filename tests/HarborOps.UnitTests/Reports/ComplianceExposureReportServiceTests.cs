using HarborOps.Api.Composition.Reports.ComplianceExposure;
using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.UnitTests.Reports;

public sealed class ComplianceExposureReportServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);

    [Fact]
    public async Task GetPageAsync_throws_when_as_of_is_missing()
    {
        var query = new FakeReportsComplianceExposureQuery(EmptyResult());
        var service = new ComplianceExposureReportService(query);

        var exception = await Assert.ThrowsAsync<ComplianceExposureReportValidationException>(
            () => service.GetPageAsync(null, null, null, 1, 25, CancellationToken.None));

        Assert.Contains(ComplianceExposureReportValidator.AsOfField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_category_is_not_recognized()
    {
        var query = new FakeReportsComplianceExposureQuery(EmptyResult());
        var service = new ComplianceExposureReportService(query);

        var exception = await Assert.ThrowsAsync<ComplianceExposureReportValidationException>(
            () => service.GetPageAsync(AsOf, null, "Bogus", 1, 25, CancellationToken.None));

        Assert.Contains(ComplianceExposureReportValidator.CategoryField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_passes_filters_through_and_composes_the_page()
    {
        var vesselId = Guid.NewGuid();
        var row = new ReportsComplianceExposureRow(
            "Certificate", vesselId, "CERT-1", "Safety Management Certificate", AsOf.AddDays(10), "Expiring", null);
        var query = new FakeReportsComplianceExposureQuery(new ReportsComplianceExposureResult([row], 1, 25, 1));
        var service = new ComplianceExposureReportService(query);

        var page = await service.GetPageAsync(AsOf, vesselId, "Certificate", 1, 25, CancellationToken.None);

        Assert.NotNull(query.LastRequest);
        Assert.Equal(AsOf, query.LastRequest.AsOf);
        Assert.Equal(vesselId, query.LastRequest.VesselId);
        Assert.Equal("Certificate", query.LastRequest.Category);

        Assert.Equal(AsOf, page.AsOf);
        var item = Assert.Single(page.Items);
        Assert.Equal("CERT-1", item.Reference);
    }

    [Fact]
    public async Task GetExportRowsAsync_requests_the_maximum_export_row_count_on_a_single_page()
    {
        var query = new FakeReportsComplianceExposureQuery(EmptyResult());
        var service = new ComplianceExposureReportService(query);

        await service.GetExportRowsAsync(AsOf, null, null, CancellationToken.None);

        Assert.NotNull(query.LastRequest);
        Assert.Equal(1, query.LastRequest.PageNumber);
        Assert.Equal(ComplianceExposureReportValidator.MaxExportRows, query.LastRequest.PageSize);
    }

    private static ReportsComplianceExposureResult EmptyResult() => new([], 1, 25, 0);
}
