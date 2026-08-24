using HarborOps.Api.Composition.Reports.DocumentsExpiry;
using HarborOps.Modules.Documents.Contracts;

namespace HarborOps.UnitTests.Reports;

public sealed class DocumentsExpiryReportServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);

    [Fact]
    public async Task GetPageAsync_throws_when_as_of_is_missing()
    {
        var query = new FakeReportsDocumentsExpiryQuery(EmptyResult());
        var service = new DocumentsExpiryReportService(query);

        var exception = await Assert.ThrowsAsync<DocumentsExpiryReportValidationException>(
            () => service.GetPageAsync(null, null, null, null, null, null, 1, 25, CancellationToken.None));

        Assert.Contains(DocumentsExpiryReportValidator.AsOfField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_page_size_exceeds_maximum()
    {
        var query = new FakeReportsDocumentsExpiryQuery(EmptyResult());
        var service = new DocumentsExpiryReportService(query);

        var exception = await Assert.ThrowsAsync<DocumentsExpiryReportValidationException>(
            () => service.GetPageAsync(
                AsOf, null, null, null, null, null, 1, DocumentsExpiryReportValidator.MaxPageSize + 1, CancellationToken.None));

        Assert.Contains(DocumentsExpiryReportValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_to_is_before_from()
    {
        var query = new FakeReportsDocumentsExpiryQuery(EmptyResult());
        var service = new DocumentsExpiryReportService(query);
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 5, 1);

        var exception = await Assert.ThrowsAsync<DocumentsExpiryReportValidationException>(
            () => service.GetPageAsync(AsOf, null, null, from, to, null, 1, 25, CancellationToken.None));

        Assert.Contains(DocumentsExpiryReportValidator.DateRangeField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_passes_filters_through_and_composes_the_page()
    {
        var vesselId = Guid.NewGuid();
        var row = new ReportsDocumentsExpiryRow(
            Guid.NewGuid(), vesselId, 7, "Safety Management Certificate", "CERT-1",
            AsOf.AddYears(-1), AsOf.AddDays(5), "Flag State Authority", "Active", 5, false);
        var query = new FakeReportsDocumentsExpiryQuery(new ReportsDocumentsExpiryResult([row], 1, 25, 1));
        var service = new DocumentsExpiryReportService(query);

        var page = await service.GetPageAsync(AsOf, vesselId, 7, null, null, "Active", 1, 25, CancellationToken.None);

        Assert.Equal(1, query.CallCount);
        Assert.NotNull(query.LastRequest);
        Assert.Equal(AsOf, query.LastRequest.AsOf);
        Assert.Equal(vesselId, query.LastRequest.VesselId);
        Assert.Equal(7, query.LastRequest.DocumentTypeId);
        Assert.Equal("Active", query.LastRequest.Status);

        Assert.Equal(AsOf, page.AsOf);
        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(row.DocumentId, item.DocumentId);
        Assert.Equal("CERT-1", item.Number);
    }

    [Fact]
    public async Task GetExportRowsAsync_throws_when_as_of_is_missing()
    {
        var query = new FakeReportsDocumentsExpiryQuery(EmptyResult());
        var service = new DocumentsExpiryReportService(query);

        await Assert.ThrowsAsync<DocumentsExpiryReportValidationException>(
            () => service.GetExportRowsAsync(null, null, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetExportRowsAsync_requests_the_maximum_export_row_count_on_a_single_page()
    {
        var query = new FakeReportsDocumentsExpiryQuery(EmptyResult());
        var service = new DocumentsExpiryReportService(query);

        await service.GetExportRowsAsync(AsOf, null, null, null, null, null, CancellationToken.None);

        Assert.NotNull(query.LastRequest);
        Assert.Equal(1, query.LastRequest.PageNumber);
        Assert.Equal(DocumentsExpiryReportValidator.MaxExportRows, query.LastRequest.PageSize);
    }

    private static ReportsDocumentsExpiryResult EmptyResult() => new([], 1, 25, 0);
}
