using HarborOps.Api.Composition.Reports.PortCallPerformance;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.UnitTests.Reports;

public sealed class PortCallPerformanceReportServiceTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);
    private static readonly DateTime DepartureUtc = new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPageAsync_requires_both_departure_dates_without_querying()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);

        var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
            service.GetPageAsync(new PortCallPerformanceReportFilter(), 1, 25, CancellationToken.None));

        Assert.Contains(PortCallPerformanceReportValidator.DepartedFromField, exception.Errors);
        Assert.Contains(PortCallPerformanceReportValidator.DepartedToField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    [Theory]
    [InlineData("2026-09-30", "2026-09-01", false)]
    [InlineData("2026-01-01", "2027-01-01", true)]
    [InlineData("2028-01-01", "2028-12-31", true)]
    [InlineData("2026-01-01", "2027-01-02", false)]
    [InlineData("2026-09-01", "2026-09-01", true)]
    public async Task GetPageAsync_accepts_ranges_of_up_to_366_inclusive_days(
        string from,
        string to,
        bool valid)
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);
        var filter = new PortCallPerformanceReportFilter
        {
            DepartedFrom = DateOnly.Parse(from),
            DepartedTo = DateOnly.Parse(to),
        };

        if (valid)
        {
            await service.GetPageAsync(filter, 1, 25, CancellationToken.None);
            Assert.Equal(1, query.CallCount);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
                service.GetPageAsync(filter, 1, 25, CancellationToken.None));
            Assert.Contains(PortCallPerformanceReportValidator.DepartedToField, exception.Errors);
            Assert.Equal(0, query.CallCount);
        }
    }

    [Fact]
    public async Task GetPageAsync_rejects_unknown_options_and_page_bounds()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);
        var filter = Filter() with { StatementAvailability = "Draft", SortBy = "Eta", SortDirection = "Up" };

        var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
            service.GetPageAsync(filter, 0, PortCallPerformanceReportValidator.MaxPageSize + 1, CancellationToken.None));

        Assert.Contains(PortCallPerformanceReportValidator.StatementAvailabilityField, exception.Errors);
        Assert.Contains(PortCallPerformanceReportValidator.SortByField, exception.Errors);
        Assert.Contains(PortCallPerformanceReportValidator.SortDirectionField, exception.Errors);
        Assert.Contains(PortCallPerformanceReportValidator.PageNumberField, exception.Errors);
        Assert.Contains(PortCallPerformanceReportValidator.PageSizeField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task GetPageAsync_passes_filters_with_canonical_option_values_and_the_cancellation_token()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);
        var voyageId = Guid.NewGuid();
        var vesselId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var filter = Filter() with
        {
            VoyageId = voyageId,
            VesselId = vesselId,
            PortId = portId,
            StatementAvailability = "notfinalized",
            SortBy = " cargoelapsed ",
            SortDirection = "DESC",
        };

        await service.GetPageAsync(filter, 3, 50, cancellation.Token);

        var request = Assert.IsType<ReportsPortCallPerformanceRequest>(query.LastRequest);
        Assert.Equal(From, request.DepartedFrom);
        Assert.Equal(To, request.DepartedTo);
        Assert.Equal(voyageId, request.VoyageId);
        Assert.Equal(vesselId, request.VesselId);
        Assert.Equal(portId, request.PortId);
        Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, request.StatementAvailability);
        Assert.Equal(ReportsPortCallPerformanceOptions.SortByCargoElapsed, request.SortBy);
        Assert.Equal(ReportsPortCallPerformanceOptions.Descending, request.SortDirection);
        Assert.Equal(3, request.PageNumber);
        Assert.Equal(50, request.PageSize);
        Assert.Equal(cancellation.Token, query.LastCancellationToken);
    }

    [Fact]
    public async Task GetPageAsync_uses_default_options_when_they_are_omitted()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);

        await service.GetPageAsync(Filter(), 1, 25, CancellationToken.None);

        var request = Assert.IsType<ReportsPortCallPerformanceRequest>(query.LastRequest);
        Assert.Equal(ReportsPortCallPerformanceOptions.AllStatements, request.StatementAvailability);
        Assert.Equal(ReportsPortCallPerformanceOptions.SortByActualDeparture, request.SortBy);
        Assert.Equal(ReportsPortCallPerformanceOptions.Ascending, request.SortDirection);
    }

    [Fact]
    public async Task GetPageAsync_adds_hours_rounded_to_two_decimals_next_to_minutes()
    {
        var finalized = FinalizedRow() with
        {
            Turnaround = Valid(50),
            ArrivalToAllFast = Valid(1),
            CargoElapsed = new ReportsPortCallMetric(null, ReportsPortCallPerformanceOptions.QualityAmbiguous, "CargoStarted"),
            CargoCompletedToDeparture = Valid(0),
            RecordedDelayMinutes = 135,
        };
        var notFinalized = NotFinalizedRow();
        var summary = EmptySummary() with
        {
            StopCount = 2,
            FinalizedCount = 1,
            NotFinalizedCount = 1,
            Turnaround = new ReportsPortCallMetricSummary(112.5m, 2, 0, 0, 0),
            ArrivalToAllFast = new ReportsPortCallMetricSummary(790m / 3m, 3, 1, 0, 0),
            RecordedDelayCount = 3,
            RecordedDelayMinutes = 135,
        };
        var query = new FakeReportsPortCallPerformanceQuery(Result([finalized, notFinalized], summary));
        var service = new PortCallPerformanceReportService(query);

        var page = await service.GetPageAsync(Filter(), 1, 25, CancellationToken.None);

        Assert.Equal(From, page.DepartedFrom);
        Assert.Equal(To, page.DepartedTo);
        var first = page.Items[0];
        Assert.Equal(new PortCallPerformanceReportMetric(50, 0.83m, ReportsPortCallPerformanceOptions.QualityValid, null), first.Turnaround);
        Assert.Equal(new PortCallPerformanceReportMetric(1, 0.02m, ReportsPortCallPerformanceOptions.QualityValid, null), first.ArrivalToAllFast);
        Assert.Equal(
            new PortCallPerformanceReportMetric(null, null, ReportsPortCallPerformanceOptions.QualityAmbiguous, "CargoStarted"),
            first.CargoElapsed);
        Assert.Equal(new PortCallPerformanceReportMetric(0, 0.00m, ReportsPortCallPerformanceOptions.QualityValid, null), first.CargoCompletedToDeparture);
        Assert.Equal(2.25m, first.RecordedDelayHours);

        var second = page.Items[1];
        Assert.Null(second.Turnaround);
        Assert.Null(second.RecordedDelayHours);
        Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, second.StatementAvailability);

        Assert.Equal(new PortCallPerformanceReportMetricSummary(112.5m, 1.88m, 2, 0, 0, 0), page.Summary.Turnaround);
        Assert.Equal(new PortCallPerformanceReportMetricSummary(263.33m, 4.39m, 3, 1, 0, 0), page.Summary.ArrivalToAllFast);
        Assert.Equal(new PortCallPerformanceReportMetricSummary(null, null, 0, 0, 0, 0), page.Summary.CargoElapsed);
        Assert.Equal(2.25m, page.Summary.RecordedDelayHours);
        Assert.Equal(1, page.Summary.NotFinalizedCount);
    }

    [Fact]
    public async Task GetPageAsync_rejects_a_filtered_set_above_the_stop_limit()
    {
        var query = new FakeReportsPortCallPerformanceQuery(
            Result([], EmptySummary(), totalCount: 25_000, stopLimitExceeded: true));
        var service = new PortCallPerformanceReportService(query);

        var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
            service.GetPageAsync(Filter(), 1, 25, CancellationToken.None));

        var message = Assert.Single(exception.Errors[PortCallPerformanceReportValidator.LimitField]);
        Assert.Contains("25,000", message);
        Assert.Contains("20,000", message);
    }

    [Fact]
    public async Task GetExportRowsAsync_requests_every_allowed_row_with_the_same_filters_and_sort()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result([FinalizedRow()], EmptySummary()));
        var service = new PortCallPerformanceReportService(query);
        var portId = Guid.NewGuid();
        var filter = Filter() with
        {
            PortId = portId,
            StatementAvailability = "Finalized",
            SortBy = "Turnaround",
            SortDirection = "Desc",
        };

        var export = await service.GetExportRowsAsync(filter, CancellationToken.None);

        var request = Assert.IsType<ReportsPortCallPerformanceRequest>(query.LastRequest);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(PortCallPerformanceReportValidator.MaxExportRows, request.PageSize);
        Assert.Equal(portId, request.PortId);
        Assert.Equal(ReportsPortCallPerformanceOptions.Finalized, request.StatementAvailability);
        Assert.Equal(ReportsPortCallPerformanceOptions.SortByTurnaround, request.SortBy);
        Assert.Equal(ReportsPortCallPerformanceOptions.Descending, request.SortDirection);
        Assert.Equal(From, export.DepartedFrom);
        Assert.Equal(To, export.DepartedTo);
        Assert.Single(export.Rows);
    }

    [Theory]
    [InlineData(10_000, false, true)]
    [InlineData(10_001, false, false)]
    [InlineData(25_000, true, false)]
    public async Task GetExportRowsAsync_rejects_exports_above_the_row_limit_instead_of_truncating(
        int totalCount,
        bool stopLimitExceeded,
        bool allowed)
    {
        var query = new FakeReportsPortCallPerformanceQuery(
            Result([FinalizedRow()], EmptySummary(), totalCount, stopLimitExceeded));
        var service = new PortCallPerformanceReportService(query);

        if (allowed)
        {
            var export = await service.GetExportRowsAsync(Filter(), CancellationToken.None);
            Assert.Single(export.Rows);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
                service.GetExportRowsAsync(Filter(), CancellationToken.None));
            var message = Assert.Single(exception.Errors[PortCallPerformanceReportValidator.LimitField]);
            Assert.Contains("10,000", message);
        }
    }

    [Fact]
    public async Task GetExportRowsAsync_validates_filters_without_querying()
    {
        var query = new FakeReportsPortCallPerformanceQuery(Result());
        var service = new PortCallPerformanceReportService(query);

        var exception = await Assert.ThrowsAsync<PortCallPerformanceReportValidationException>(() =>
            service.GetExportRowsAsync(Filter() with { SortBy = "Eta" }, CancellationToken.None));

        Assert.Contains(PortCallPerformanceReportValidator.SortByField, exception.Errors);
        Assert.Equal(0, query.CallCount);
    }

    private static PortCallPerformanceReportFilter Filter() => new() { DepartedFrom = From, DepartedTo = To };

    private static ReportsPortCallMetric Valid(int minutes) =>
        new(minutes, ReportsPortCallPerformanceOptions.QualityValid, null);

    private static ReportsPortCallPerformanceRow FinalizedRow() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "V-2026-014",
        2,
        "Rotterdam",
        "NLRTM",
        "Euromax 3",
        false,
        DepartureUtc.AddHours(-12),
        DepartureUtc,
        ReportsPortCallPerformanceOptions.Finalized,
        Guid.NewGuid(),
        DepartureUtc.AddDays(1),
        Valid(720),
        Valid(90),
        Valid(480),
        Valid(120),
        1,
        45);

    private static ReportsPortCallPerformanceRow NotFinalizedRow() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "V-2026-023",
        1,
        "Hamburg",
        "DEHAM",
        null,
        true,
        DepartureUtc.AddHours(-10),
        DepartureUtc,
        ReportsPortCallPerformanceOptions.NotFinalized,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private static ReportsPortCallPerformanceSummary EmptySummary()
    {
        var empty = new ReportsPortCallMetricSummary(null, 0, 0, 0, 0);

        return new ReportsPortCallPerformanceSummary(0, 0, 0, empty, empty, empty, empty, 0, 0);
    }

    private static ReportsPortCallPerformanceResult Result() => Result([], EmptySummary());

    private static ReportsPortCallPerformanceResult Result(
        IReadOnlyList<ReportsPortCallPerformanceRow> rows,
        ReportsPortCallPerformanceSummary summary,
        int? totalCount = null,
        bool stopLimitExceeded = false) =>
        new(rows, 1, 25, totalCount ?? rows.Count, stopLimitExceeded, summary, []);
}
