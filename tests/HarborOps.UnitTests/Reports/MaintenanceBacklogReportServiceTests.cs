using HarborOps.Api.Composition.Reports.MaintenanceBacklog;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Reports;

public sealed class MaintenanceBacklogReportServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);
    private static readonly DateTime DueUtc = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPageAsync_throws_when_as_of_is_missing()
    {
        var query = new FakeReportsMaintenanceBacklogQuery(EmptyResult());
        var service = new MaintenanceBacklogReportService(query);

        var exception = await Assert.ThrowsAsync<MaintenanceBacklogReportValidationException>(
            () => service.GetPageAsync(null, null, null, null, null, null, null, 1, 25, CancellationToken.None));

        Assert.Contains(MaintenanceBacklogReportValidator.AsOfField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_priority_is_not_recognized()
    {
        var query = new FakeReportsMaintenanceBacklogQuery(EmptyResult());
        var service = new MaintenanceBacklogReportService(query);

        var exception = await Assert.ThrowsAsync<MaintenanceBacklogReportValidationException>(
            () => service.GetPageAsync(AsOf, null, "Urgent", null, null, null, null, 1, 25, CancellationToken.None));

        Assert.Contains(MaintenanceBacklogReportValidator.PriorityField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_status_is_not_recognized()
    {
        var query = new FakeReportsMaintenanceBacklogQuery(EmptyResult());
        var service = new MaintenanceBacklogReportService(query);

        var exception = await Assert.ThrowsAsync<MaintenanceBacklogReportValidationException>(
            () => service.GetPageAsync(AsOf, null, null, "Blocked", null, null, null, 1, 25, CancellationToken.None));

        Assert.Contains(MaintenanceBacklogReportValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_throws_when_due_to_is_before_due_from()
    {
        var query = new FakeReportsMaintenanceBacklogQuery(EmptyResult());
        var service = new MaintenanceBacklogReportService(query);
        var dueFrom = new DateOnly(2026, 6, 1);
        var dueTo = new DateOnly(2026, 5, 1);

        var exception = await Assert.ThrowsAsync<MaintenanceBacklogReportValidationException>(
            () => service.GetPageAsync(AsOf, null, null, null, null, dueFrom, dueTo, 1, 25, CancellationToken.None));

        Assert.Contains(MaintenanceBacklogReportValidator.DateRangeField, exception.Errors);
    }

    [Fact]
    public async Task GetPageAsync_passes_filters_through_and_composes_the_page()
    {
        var vesselId = Guid.NewGuid();
        var row = new ReportsMaintenanceBacklogRow(
            Guid.NewGuid(), vesselId, "Replace impeller", "Critical", "Open", DueUtc, true, null, DueUtc.AddDays(-10), null);
        var query = new FakeReportsMaintenanceBacklogQuery(new ReportsMaintenanceBacklogResult([row], 1, 25, 1));
        var service = new MaintenanceBacklogReportService(query);

        var page = await service.GetPageAsync(
            AsOf, vesselId, "Critical", "Open", true, null, null, 1, 25, CancellationToken.None);

        Assert.NotNull(query.LastRequest);
        Assert.Equal(AsOf, query.LastRequest.AsOf);
        Assert.Equal(vesselId, query.LastRequest.VesselId);
        Assert.Equal("Critical", query.LastRequest.Priority);
        Assert.Equal("Open", query.LastRequest.Status);
        Assert.True(query.LastRequest.Overdue);

        Assert.Equal(AsOf, page.AsOf);
        var item = Assert.Single(page.Items);
        Assert.Equal("Replace impeller", item.Title);
        Assert.True(item.IsOverdue);
    }

    [Fact]
    public async Task GetExportRowsAsync_requests_the_maximum_export_row_count_on_a_single_page()
    {
        var query = new FakeReportsMaintenanceBacklogQuery(EmptyResult());
        var service = new MaintenanceBacklogReportService(query);

        await service.GetExportRowsAsync(AsOf, null, null, null, null, null, null, CancellationToken.None);

        Assert.NotNull(query.LastRequest);
        Assert.Equal(1, query.LastRequest.PageNumber);
        Assert.Equal(MaintenanceBacklogReportValidator.MaxExportRows, query.LastRequest.PageSize);
    }

    private static ReportsMaintenanceBacklogResult EmptyResult() => new([], 1, 25, 0);
}
