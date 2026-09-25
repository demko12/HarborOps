using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Reports.PortCallPerformance;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Reports;

public sealed class PortCallPerformanceReportEndpointTests : IntegrationTestBase
{
    private const string ReportUrl = "/api/reports/port-call-performance";
    private const string ExportUrl = "/api/reports/port-call-performance/export";
    private const string September = "departedFrom=2026-09-01&departedTo=2026-09-30";

    private static readonly DateTime ArrivalUtc = new(2026, 9, 2, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DepartureUtc = new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public PortCallPerformanceReportEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_finalized_metrics_not_finalized_stops_and_full_set_summaries()
    {
        var finalized = await SeedKnownFinalizedStopAsync("Rotterdam", "Euromax 3");
        var notFinalized = await SeedDepartedStopAsync(
            "Hamburg",
            new DateTime(2026, 9, 17, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 18, 6, 0, 0, DateTimeKind.Utc));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync($"{ReportUrl}?{September}", CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<PortCallPerformanceReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal([finalized.StopId, notFinalized], page.Items.Select(row => row.VoyageStopId));

        var first = page.Items[0];
        Assert.Equal(finalized.StatementId, first.StatementId);
        Assert.Equal(Valid(720, 12.00m), first.Turnaround);
        Assert.Equal(Valid(90, 1.50m), first.ArrivalToAllFast);
        Assert.Equal(Valid(480, 8.00m), first.CargoElapsed);
        Assert.Equal(Valid(120, 2.00m), first.CargoCompletedToDeparture);
        Assert.Equal(0.75m, first.RecordedDelayHours);
        Assert.Equal("Euromax 3", first.BerthName);

        var second = page.Items[1];
        Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, second.StatementAvailability);
        Assert.Null(second.Turnaround);
        Assert.True(second.UsesLiveValues);

        Assert.Equal(2, page.Summary.StopCount);
        Assert.Equal(1, page.Summary.FinalizedCount);
        Assert.Equal(1, page.Summary.NotFinalizedCount);
        Assert.Equal(new PortCallPerformanceReportMetricSummary(720m, 12.00m, 1, 0, 0, 0), page.Summary.Turnaround);
        Assert.Equal(["Hamburg", "Rotterdam"], page.Ports.Select(port => port.PortName));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, Permissions.Reports.Read, Permissions.Voyages.Read)]
    [InlineData(HttpStatusCode.OK, Permissions.Reports.Read, Permissions.Voyages.Manage)]
    [InlineData(HttpStatusCode.OK, Permissions.Reports.Manage, Permissions.Voyages.Read)]
    [InlineData(HttpStatusCode.Forbidden, Permissions.Reports.Read)]
    [InlineData(HttpStatusCode.Forbidden, Permissions.Reports.Manage)]
    [InlineData(HttpStatusCode.Forbidden, Permissions.Voyages.Read)]
    [InlineData(HttpStatusCode.Forbidden, Permissions.Voyages.Manage)]
    [InlineData(HttpStatusCode.Forbidden, Permissions.Reports.Read, Permissions.Maintenance.Read)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Report_and_export_require_both_reports_and_voyages_read(
        HttpStatusCode expected,
        params string[] permissions)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(permissions);

        using var report = await client.GetAsync($"{ReportUrl}?{September}", CancellationToken.None);
        using var export = await client.GetAsync($"{ExportUrl}?{September}", CancellationToken.None);

        Assert.Equal(expected, report.StatusCode);
        Assert.Equal(expected, export.StatusCode);
    }

    [Fact]
    public async Task Report_and_export_reject_anonymous_requests()
    {
        using var client = _application.CreateClient();

        using var report = await client.GetAsync($"{ReportUrl}?{September}", CancellationToken.None);
        using var export = await client.GetAsync($"{ExportUrl}?{September}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, report.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, export.StatusCode);
    }

    [Theory]
    [InlineData(ReportUrl, "", PortCallPerformanceReportValidator.DepartedFromField)]
    [InlineData(ReportUrl, "departedFrom=2026-01-01&departedTo=2027-01-02", PortCallPerformanceReportValidator.DepartedToField)]
    [InlineData(ReportUrl, September + "&statementAvailability=Draft", PortCallPerformanceReportValidator.StatementAvailabilityField)]
    [InlineData(ReportUrl, September + "&sortBy=Eta", PortCallPerformanceReportValidator.SortByField)]
    [InlineData(ReportUrl, September + "&pageSize=101", PortCallPerformanceReportValidator.PageSizeField)]
    [InlineData(ExportUrl, "departedFrom=2026-01-01&departedTo=2027-01-02", PortCallPerformanceReportValidator.DepartedToField)]
    [InlineData(ExportUrl, September + "&sortDirection=Up", PortCallPerformanceReportValidator.SortDirectionField)]
    public async Task Invalid_filters_return_a_validation_problem(string url, string queryString, string field)
    {
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync($"{url}?{queryString}", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(field, problem.Errors);
    }

    [Fact]
    public async Task Export_returns_csv_with_the_same_values_and_order_as_the_report()
    {
        var finalized = await SeedKnownFinalizedStopAsync("Rotterdam", "Euromax 3");
        var notFinalized = await SeedDepartedStopAsync(
            "Hamburg",
            new DateTime(2026, 9, 17, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 18, 6, 0, 0, DateTimeKind.Utc));
        const string filters = September + "&sortBy=turnaround&sortDirection=desc";

        using var client = await CreateReportClientAsync();
        using var json = await client.GetAsync($"{ReportUrl}?{filters}", CancellationToken.None);
        var page = await json.Content.ReadFromJsonAsync<PortCallPerformanceReportPage>(CancellationToken.None);
        using var response = await client.GetAsync($"{ExportUrl}?{filters}", CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "port-call-performance-2026-09-01-to-2026-09-30.csv",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var rows = ParseCsv(csv);
        Assert.NotNull(page);
        Assert.Equal(page.Items.Select(row => row.VoyageStopId.ToString()), rows.Select(row => row["VoyageStopId"]));
        Assert.Equal([finalized.StopId.ToString(), notFinalized.ToString()], rows.Select(row => row["VoyageStopId"]));

        var first = rows[0];
        var firstJson = page.Items[0];
        Assert.Equal(firstJson.Turnaround!.Hours!.Value.ToString("0.00", CultureInfo.InvariantCulture), first["TurnaroundHours"]);
        Assert.Equal("720", first["TurnaroundMinutes"]);
        Assert.Equal("12.00", first["TurnaroundHours"]);
        Assert.Equal("1.50", first["ArrivalToAllFastHours"]);
        Assert.Equal(ReportsPortCallPerformanceOptions.QualityValid, first["CargoElapsedQuality"]);
        Assert.Equal("0.75", first["RecordedDelayHours"]);
        Assert.Equal("2026-09-03T08:00:00Z", first["ActualDepartureUtc"]);
        Assert.Equal(finalized.StatementId.ToString(), first["StatementId"]);
        Assert.Equal("Finalized", first["StatementAvailability"]);

        var second = rows[1];
        Assert.Equal("NotFinalized", second["StatementAvailability"]);
        Assert.Equal(string.Empty, second["TurnaroundMinutes"]);
        Assert.Equal(string.Empty, second["TurnaroundQuality"]);
        Assert.Equal(string.Empty, second["StatementId"]);
        Assert.Equal("True", second["UsesLiveValues"]);
    }

    [Fact]
    public async Task Export_escapes_formula_prefixes_in_snapshot_labels()
    {
        await SeedKnownFinalizedStopAsync("=HYPERLINK(1)", "@SUM(1)");

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync($"{ExportUrl}?{September}", CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        var row = Assert.Single(ParseCsv(csv));
        Assert.Equal("'=HYPERLINK(1)", row["Port"]);
        Assert.Equal("'@SUM(1)", row["Berth"]);
    }

    private static PortCallPerformanceReportMetric Valid(int minutes, decimal hours) =>
        new(minutes, hours, ReportsPortCallPerformanceOptions.QualityValid, null);

    private static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Replace("﻿", string.Empty).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');

        return lines
            .Skip(1)
            .Select(line => line.Split(','))
            .Select(values => header
                .Select((name, index) => (name, value: values[index]))
                .ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal))
            .ToList();
    }

    private Task<HttpClient> CreateReportClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read,
            Permissions.Voyages.Read);
    }

    private async Task<(Guid StopId, Guid StatementId)> SeedKnownFinalizedStopAsync(string portName, string berthName)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var port = Port.Create(portName, "Netherlands", unLocode: "NLRTM");
        var voyage = Voyage.Schedule(
            Guid.NewGuid(), "V-2026-014", "NLRTM", "DEHAM", ArrivalUtc.AddDays(-2), DepartureUtc.AddDays(2));
        var stop = VoyageStop.Create(voyage.Id, port.Id, 2, ArrivalUtc, DepartureUtc);
        stop.RecordArrival(ArrivalUtc);
        stop.RecordDeparture(DepartureUtc);

        var statement = PortCallStatement.CreateDraft(voyage.Id, stop.Id, Guid.NewGuid(), DepartureUtc);
        var entries = statement.Finalize(
            new PortCallStatementSnapshot(
                voyage.VoyageNumber,
                stop.StopOrder,
                port.Name,
                port.UnLocode,
                null,
                berthName,
                ArrivalUtc,
                DepartureUtc,
                new string('C', 64),
                [
                    Milestone(PortCallActivityType.AllFast, ArrivalUtc.AddMinutes(90)),
                    Milestone(PortCallActivityType.CargoStarted, ArrivalUtc.AddHours(2)),
                    new PortCallLogEntry(
                        PortCallLogSource.Delay, Guid.NewGuid(), null, 0, "Tug", ArrivalUtc.AddHours(3), null, null, 45),
                    Milestone(PortCallActivityType.CargoCompleted, ArrivalUtc.AddHours(10)),
                ]),
            Guid.NewGuid(),
            "reports@harborops.test",
            DepartureUtc.AddDays(1));

        dbContext.AddRange(port, voyage, stop, statement);
        dbContext.AddRange(entries);
        await dbContext.SaveChangesAsync();

        return (stop.Id, statement.Id);
    }

    private async Task<Guid> SeedDepartedStopAsync(string portName, DateTime arrivalUtc, DateTime departureUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var port = Port.Create(portName, "Germany", unLocode: "DEHAM");
        var voyage = Voyage.Schedule(
            Guid.NewGuid(), "V-2026-023", "DEHAM", "NLRTM", arrivalUtc.AddDays(-2), departureUtc.AddDays(2));
        var stop = VoyageStop.Create(voyage.Id, port.Id, 1, arrivalUtc, departureUtc);
        stop.RecordArrival(arrivalUtc);
        stop.RecordDeparture(departureUtc);

        dbContext.AddRange(port, voyage, stop);
        await dbContext.SaveChangesAsync();

        return stop.Id;
    }

    private static PortCallLogEntry Milestone(string activityType, DateTime occurredAtUtc) =>
        new(PortCallLogSource.ManualActivity, Guid.NewGuid(), 1, 0, activityType, occurredAtUtc, null, null, null);
}
