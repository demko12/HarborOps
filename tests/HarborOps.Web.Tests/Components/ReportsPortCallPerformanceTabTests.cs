using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Reports;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class ReportsPortCallPerformanceTabTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string VoyageId = "30000000-0000-0000-0000-000000000003";
    private const string FinalizedStopId = "10000000-0000-0000-0000-000000000001";
    private const string NotFinalizedStopId = "10000000-0000-0000-0000-000000000002";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPortCallPerformance =
        _ => Json(HttpStatusCode.OK, Page(2, FinalizedRow(), NotFinalizedRow()));

    public ReportsPortCallPerformanceTabTests()
    {
        ConfigureAuthorization(
            CurrentUserAuthorizationState.ReportsReadPermission,
            CurrentUserAuthorizationState.VoyagesReadPermission);
    }

    [Fact]
    public void A_user_with_reports_and_voyages_access_opens_the_port_call_tab_with_the_last_30_days()
    {
        var page = RenderPage();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        page.WaitForAssertion(() =>
        {
            var tab = Assert.Single(page.FindAll(".workspace-tab"));
            Assert.Contains("Port-call performance", tab.TextContent, StringComparison.Ordinal);
            var query = requestedUris.Last(IsPortCallRequest).Query;
            Assert.Contains($"departedFrom={today.AddDays(-30):yyyy-MM-dd}", query, StringComparison.Ordinal);
            Assert.Contains($"departedTo={today:yyyy-MM-dd}", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("#reports-as-of"));
        });
    }

    [Fact]
    public void The_tab_is_hidden_without_voyages_access()
    {
        ConfigureAuthorization(
            CurrentUserAuthorizationState.ReportsReadPermission,
            CurrentUserAuthorizationState.MaintenanceReadPermission);

        var page = RenderPage<ReportsMaintenanceBacklog>();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(page.FindAll(".workspace-tab"), tab => tab.TextContent.Contains("Port-call performance", StringComparison.Ordinal));
            Assert.DoesNotContain(requestedUris, IsPortCallRequest);
        });
    }

    [Fact]
    public void A_pending_request_shows_the_loading_state()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        respondToPortCallPerformance = _ => pending.Task;

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("Loading report...", page.Markup, StringComparison.Ordinal));
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page(0), Encoding.UTF8, "application/json"),
        });
    }

    [Fact]
    public void Summaries_show_means_with_sample_counts_and_data_quality_exclusions()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            var strip = page.Find(".reports-summary-strip").TextContent;
            Assert.Contains("2 stops", strip, StringComparison.Ordinal);
            Assert.Contains("1 finalized", strip, StringComparison.Ordinal);
            Assert.Contains("1 not finalized (excluded from metrics)", strip, StringComparison.Ordinal);
            Assert.Contains("1 recorded delay · 0.75 h", strip, StringComparison.Ordinal);
            Assert.Contains("(not deducted from turnaround)", strip, StringComparison.Ordinal);

            var kpis = page.Find(".reports-kpis").TextContent;
            Assert.Equal(4, page.FindAll(".reports-kpis .reports-kpi").Count);
            Assert.Contains("12.00 h", kpis, StringComparison.Ordinal);
            Assert.Contains("from 1 of 2 stops · 1 not finalized", kpis, StringComparison.Ordinal);
            Assert.Contains("Avg arrival → all fast", kpis, StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 missing a milestone · 1 not finalized", kpis, StringComparison.Ordinal);
            Assert.Contains("Avg cargo elapsed", kpis, StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 with a duplicated milestone · 1 not finalized", kpis, StringComparison.Ordinal);
            Assert.Contains("Avg cargo completed → departure", kpis, StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 out of order · 1 not finalized", kpis, StringComparison.Ordinal);
            Assert.Equal(
                ["Elapsed time", "Average elapsed time"],
                page.FindAll("th.reports-group").Select(header => header.TextContent.Trim()).OrderBy(text => text.Length).ToArray());

            var port = Assert.Single(page.FindAll("table.reports-port-table tbody tr"));
            var cells = port.QuerySelectorAll("td").Select(cell => cell.TextContent).ToArray();
            Assert.Equal(7, cells.Length);
            Assert.Contains("Rotterdam", cells[0], StringComparison.Ordinal);
            Assert.Contains("NLRTM", cells[0], StringComparison.Ordinal);
            Assert.Contains("1 finalized · 1 not finalized", cells[1], StringComparison.Ordinal);
            Assert.Contains("12.00 h", cells[2], StringComparison.Ordinal);
            Assert.Contains("from 1 of 2 stops", cells[2], StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 missing a milestone", cells[3], StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 with a duplicated milestone", cells[4], StringComparison.Ordinal);
            Assert.Contains("from 0 of 2 stops · 1 out of order", cells[5], StringComparison.Ordinal);
            Assert.All(cells[2..6], cell => Assert.DoesNotContain("not finalized", cell, StringComparison.Ordinal));
            Assert.Contains("1 · 0.75 h", cells[6], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Reloading_after_a_sort_keeps_the_current_results_visible_until_the_new_page_arrives()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("table.reports-detail-table tbody tr").Count));

        var pending = new TaskCompletionSource<HttpResponseMessage>();
        respondToPortCallPerformance = _ => pending.Task;

        page.FindAll(".reports-detail-table .reports-sort").Single(button => button.TextContent.Contains("Turnaround", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(() =>
        {
            var results = page.Find(".reports-results");
            Assert.Contains("reports-refreshing", results.ClassList);
            Assert.Equal("true", results.GetAttribute("aria-busy"));
            Assert.DoesNotContain("Loading report...", page.Markup, StringComparison.Ordinal);
            Assert.Equal(2, page.FindAll("table.reports-detail-table tbody tr").Count);
            Assert.NotEmpty(page.FindAll(".reports-kpis"));
            Assert.NotEmpty(page.FindAll("table.reports-port-table"));
        });

        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page(1, FinalizedRow()), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() =>
        {
            var results = page.Find(".reports-results");
            Assert.DoesNotContain("reports-refreshing", results.ClassList);
            Assert.Equal("false", results.GetAttribute("aria-busy"));
            Assert.Single(page.FindAll("table.reports-detail-table tbody tr"));
        });
    }

    [Fact]
    public void Port_summaries_sort_in_the_browser_with_unavailable_means_last()
    {
        respondToPortCallPerformance = _ => Json(HttpStatusCode.OK, PageWithPorts(
            PortSummary("50000000-0000-0000-0000-000000000001", "Genoa", stops: 3, turnaroundHours: "30.00", delayMinutes: 90),
            PortSummary("50000000-0000-0000-0000-000000000002", "Antwerp", stops: 1, turnaroundHours: null, delayMinutes: 0),
            PortSummary("50000000-0000-0000-0000-000000000003", "Valencia", stops: 2, turnaroundHours: "12.50", delayMinutes: 30)));

        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Equal(["Antwerp", "Genoa", "Valencia"], PortNames(page)));
        var requestsBeforeSorting = requestedUris.Count(IsPortCallRequest);

        ClickPortSort(page, "Turnaround");
        page.WaitForAssertion(() =>
        {
            Assert.Equal(["Valencia", "Genoa", "Antwerp"], PortNames(page));
            Assert.Equal("ascending", PortHeader(page, "Turnaround").GetAttribute("aria-sort"));
            Assert.Contains("None", PortRow(page, "Antwerp").QuerySelectorAll("td").Last().TextContent, StringComparison.Ordinal);
            Assert.Contains("1 · 1.50 h", PortRow(page, "Genoa").QuerySelectorAll("td").Last().TextContent, StringComparison.Ordinal);
        });

        ClickPortSort(page, "Turnaround");
        page.WaitForAssertion(() =>
        {
            Assert.Equal(["Genoa", "Valencia", "Antwerp"], PortNames(page));
            Assert.Equal("descending", PortHeader(page, "Turnaround").GetAttribute("aria-sort"));
        });

        ClickPortSort(page, "Stops");
        page.WaitForAssertion(() => Assert.Equal(["Antwerp", "Valencia", "Genoa"], PortNames(page)));

        ClickPortSort(page, "Recorded delays");
        ClickPortSort(page, "Recorded delays");
        page.WaitForAssertion(() => Assert.Equal(["Genoa", "Valencia", "Antwerp"], PortNames(page)));

        Assert.Equal(requestsBeforeSorting, requestedUris.Count(IsPortCallRequest));
    }

    [Fact]
    public void Port_chart_ranks_ports_by_average_turnaround_and_the_ring_shows_the_finalized_share()
    {
        respondToPortCallPerformance = _ => Json(HttpStatusCode.OK, PageWithPorts(
            PortSummary("50000000-0000-0000-0000-000000000001", "Genoa", stops: 3, turnaroundHours: "30.00", delayMinutes: 90),
            PortSummary("50000000-0000-0000-0000-000000000002", "Antwerp", stops: 1, turnaroundHours: null, delayMinutes: 0),
            PortSummary("50000000-0000-0000-0000-000000000003", "Valencia", stops: 2, turnaroundHours: "12.50", delayMinutes: 30)));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            var bars = page.FindAll(".reports-port-bar");
            Assert.Equal(["Genoa", "Valencia"], bars.Select(bar => bar.QuerySelector(".reports-port-bar-name > span")!.TextContent));
            Assert.Equal(["30.00 h", "12.50 h"], bars.Select(bar => bar.QuerySelector("strong")!.TextContent));
            Assert.Contains("50%", page.Find(".reports-ring").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Turnaround_bar_splits_the_mean_into_phases_scaled_to_the_longest_port()
    {
        var port = new PortCallPerformanceReportPortSummary(
            Guid.NewGuid(),
            "Rotterdam",
            null,
            new PortCallPerformanceReportSummary(
                4, 4, 0,
                new PortCallPerformanceReportMetricSummary(null, 20m, 4, 0, 0, 0),
                new PortCallPerformanceReportMetricSummary(null, 2m, 4, 0, 0, 0),
                new PortCallPerformanceReportMetricSummary(null, 12m, 4, 0, 0, 0),
                new PortCallPerformanceReportMetricSummary(null, 4m, 4, 0, 0, 0),
                0, 0, 0m));

        var bar = ReportsPortCallPerformance.BuildTurnaroundBar(port, longest: 40m);

        Assert.Equal(["phase-all-fast", "phase-cargo", "phase-departure", "phase-other"], bar.Segments.Select(segment => segment.CssClass));
        Assert.Equal([5m, 30m, 10m, 5m], bar.Segments.Select(segment => segment.WidthPercent));
        Assert.Equal("4 stops", bar.StopLabel);
    }

    private static string[] PortNames(IRenderedComponent<ReportsPortCallPerformance> page) =>
        page.FindAll("table.reports-port-table tbody tr")
            .Select(row => row.QuerySelector("td")!.ChildNodes
                .OfType<AngleSharp.Dom.IText>()
                .Select(text => text.Text.Trim())
                .First(text => text.Length > 0))
            .ToArray();

    private static AngleSharp.Dom.IElement PortRow(IRenderedComponent<ReportsPortCallPerformance> page, string portName) =>
        page.FindAll("table.reports-port-table tbody tr")
            .Single(row => row.QuerySelector("td")!.TextContent.Contains(portName, StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement PortHeader(IRenderedComponent<ReportsPortCallPerformance> page, string label) =>
        page.FindAll("table.reports-port-table th")
            .Single(header => header.TextContent.Contains(label, StringComparison.Ordinal));

    private static void ClickPortSort(IRenderedComponent<ReportsPortCallPerformance> page, string label) =>
        PortHeader(page, label).QuerySelector("button")!.Click();

    [Fact]
    public void Rows_show_hours_data_quality_badges_and_statement_source_links()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            var rows = page.FindAll("table.reports-detail-table tbody tr");
            Assert.Equal(2, rows.Count);

            var finalized = rows[0];
            Assert.Contains("V-2026-014", finalized.TextContent, StringComparison.Ordinal);
            Assert.Contains("Aurora Borealis", finalized.TextContent, StringComparison.Ordinal);
            Assert.Contains("03 Sep 2026 08:00", finalized.TextContent, StringComparison.Ordinal);
            Assert.Contains("12.00 h", finalized.TextContent, StringComparison.Ordinal);
            Assert.Contains("1 · 0.75 h", finalized.TextContent, StringComparison.Ordinal);

            var badges = finalized.QuerySelectorAll(".reports-status").Select(badge => badge.TextContent).ToArray();
            Assert.Equal(["Missing", "Ambiguous", "Invalid"], badges);
            Assert.Contains(
                "all fast",
                finalized.QuerySelector(".reports-status-warning")!.GetAttribute("title"),
                StringComparison.Ordinal);

            var link = finalized.QuerySelector("a.reports-source-link")!;
            Assert.Equal($"/voyages/{VoyageId}/stops/{FinalizedStopId}/statement/print", link.GetAttribute("href"));
            Assert.Contains("Finalized 04 Sep 2026 09:12", finalized.TextContent, StringComparison.Ordinal);

            var notFinalized = rows[1];
            Assert.Contains("Not finalized", notFinalized.TextContent, StringComparison.Ordinal);
            Assert.Contains("(live)", notFinalized.TextContent, StringComparison.Ordinal);
            Assert.Null(notFinalized.QuerySelector("a.reports-source-link"));
            Assert.DoesNotContain(" h", notFinalized.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_empty_result_shows_the_empty_state()
    {
        respondToPortCallPerformance = _ => Json(HttpStatusCode.OK, Page(0));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("No records match your filters", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("table.reports-table"));
        });
    }

    [Theory]
    [InlineData("2026-09-30", "2026-09-01", "must be on or after")]
    [InlineData("2025-01-01", "2026-01-02", "must not exceed 366 days")]
    public void An_invalid_departure_range_shows_a_message_without_calling_the_api(
        string from,
        string to,
        string expectedMessage)
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsPortCallRequest));

        page.Find("#reports-departed-to").Change(to);
        var requestsBefore = requestedUris.Count(IsPortCallRequest);
        page.Find("#reports-departed-from").Change(from);

        page.WaitForAssertion(() =>
            Assert.Contains(expectedMessage, page.Find(".reports-validation").TextContent, StringComparison.Ordinal));
        Assert.Equal(requestsBefore, requestedUris.Count(IsPortCallRequest));
    }

    [Fact]
    public void A_server_side_limit_is_shown_as_a_filter_message_instead_of_an_empty_result()
    {
        respondToPortCallPerformance = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"limit":["25,000 stops match these filters; the report is limited to 20,000."]}}""");

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("limited to 20,000", page.Find(".reports-validation").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("No records match your filters", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        var attempts = 0;
        respondToPortCallPerformance = _ => ++attempts == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
            : Json(HttpStatusCode.OK, Page(2, FinalizedRow(), NotFinalizedRow()));

        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state button")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("table.reports-detail-table tbody tr").Count));
    }

    [Fact]
    public void Clicking_a_column_header_sorts_and_toggles_the_direction()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".reports-detail-table .reports-sort")));

        page.FindAll(".reports-detail-table .reports-sort").Single(button => button.TextContent.Contains("Turnaround", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(IsPortCallRequest).Query;
            Assert.Contains("sortBy=Turnaround", query, StringComparison.Ordinal);
            Assert.Contains("sortDirection=Asc", query, StringComparison.Ordinal);
            Assert.Equal("ascending", TurnaroundHeader(page).GetAttribute("aria-sort"));
        });

        page.FindAll(".reports-detail-table .reports-sort").Single(button => button.TextContent.Contains("Turnaround", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("sortDirection=Desc", requestedUris.Last(IsPortCallRequest).Query, StringComparison.Ordinal);
            Assert.Equal("descending", TurnaroundHeader(page).GetAttribute("aria-sort"));
        });
    }

    [Fact]
    public void Clicking_a_voyage_number_filters_to_that_voyage_until_the_chip_is_removed()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".reports-link-button")));

        page.FindAll(".reports-link-button")[0].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains($"voyageId={VoyageId}", requestedUris.Last(IsPortCallRequest).Query, StringComparison.Ordinal);
            Assert.Contains("V-2026-014", page.Find(".reports-filter-chip").TextContent, StringComparison.Ordinal);
        });

        page.Find(".reports-filter-chip").Click();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("voyageId=", requestedUris.Last(IsPortCallRequest).Query, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".reports-filter-chip"));
        });
    }

    [Fact]
    public void The_statement_filter_and_sort_are_sent_and_reflected_in_the_download_link()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsPortCallRequest));

        page.Find("#reports-statement").Change("Finalized");
        page.Find("#reports-departed-from").Change("2026-09-01");
        page.Find("#reports-departed-to").Change("2026-09-30");

        page.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(IsPortCallRequest).Query;
            Assert.Contains("statementAvailability=Finalized", query, StringComparison.Ordinal);
            Assert.Contains("departedFrom=2026-09-01", query, StringComparison.Ordinal);

            var href = page.Find("a.reports-download-button").GetAttribute("href")!;
            Assert.StartsWith("/reports/download/port-call-performance?", href, StringComparison.Ordinal);
            Assert.Contains("departedFrom=2026-09-01", href, StringComparison.Ordinal);
            Assert.Contains("departedTo=2026-09-30", href, StringComparison.Ordinal);
            Assert.Contains("statementAvailability=Finalized", href, StringComparison.Ordinal);
            Assert.Contains("sortBy=ActualDeparture", href, StringComparison.Ordinal);
            Assert.DoesNotContain("pageNumber=", href, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Export_is_disabled_with_an_explanation_when_more_than_10000_stops_match()
    {
        respondToPortCallPerformance = _ => Json(HttpStatusCode.OK, Page(10_001, FinalizedRow()));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            var button = page.Find(".reports-download-button");
            Assert.Equal("button", button.TagName, ignoreCase: true);
            Assert.True(button.HasAttribute("disabled"));
            Assert.Contains("10,001 stops match", page.Find("#reports-export-limit").TextContent, StringComparison.Ordinal);
            Assert.Contains("limited to 10,000 rows", page.Find("#reports-export-limit").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Export_stays_available_at_exactly_10000_stops()
    {
        respondToPortCallPerformance = _ => Json(HttpStatusCode.OK, Page(10_000, FinalizedRow()));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("a", page.Find(".reports-download-button").TagName, ignoreCase: true);
            Assert.Empty(page.FindAll("#reports-export-limit"));
        });
    }

    [Fact]
    public void Reset_restores_the_default_range_and_clears_filters()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsPortCallRequest));
        page.Find("#reports-statement").Change("NotFinalized");
        page.Find("#reports-departed-from").Change("2026-01-01");
        page.WaitForAssertion(() =>
            Assert.Contains("departedFrom=2026-01-01", requestedUris.Last(IsPortCallRequest).Query, StringComparison.Ordinal));

        page.Find(".workspace-filter-reset").Click();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        page.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(IsPortCallRequest).Query;
            Assert.Contains($"departedFrom={today.AddDays(-30):yyyy-MM-dd}", query, StringComparison.Ordinal);
            Assert.Contains("statementAvailability=All", query, StringComparison.Ordinal);
        });
    }

    private static AngleSharp.Dom.IElement TurnaroundHeader(IRenderedComponent<ReportsPortCallPerformance> page) =>
        page.FindAll("table.reports-detail-table th")
            .Single(header => header.TextContent.Contains("Turnaround", StringComparison.Ordinal));

    private static bool IsPortCallRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/port-call-performance", StringComparison.Ordinal);

    private void ConfigureAuthorization(params string[] permissions)
    {
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();
    }

    private IRenderedComponent<ReportsPortCallPerformance> RenderPage() => RenderPage<ReportsPortCallPerformance>();

    private IRenderedComponent<TPage> RenderPage<TPage>()
        where TPage : IComponent
    {
        var reportsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return IsPortCallRequest(request.RequestUri!)
                ? respondToPortCallPerformance(request)
                : Json(HttpStatusCode.OK, """{"asOf":"2026-08-15","items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var voyagesHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Json(HttpStatusCode.OK, """{"items":[]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new ReportsApiClient(reportsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(new VoyagesApiClient(voyagesHttpClient));

        return Render<TPage>();
    }

    private static string Metric(int? minutes, string quality = "Valid", string? milestone = null)
    {
        var hours = minutes is { } value
            ? (value / 60m).ToString("0.00", CultureInfo.InvariantCulture)
            : "null";
        var minutesJson = minutes?.ToString(CultureInfo.InvariantCulture) ?? "null";
        var milestoneJson = milestone is null ? "null" : $"\"{milestone}\"";

        return $$"""{"minutes":{{minutesJson}},"hours":{{hours}},"quality":"{{quality}}","milestone":{{milestoneJson}}}""";
    }

    private static string FinalizedRow() =>
        $$"""
        {"voyageStopId":"{{FinalizedStopId}}","voyageId":"{{VoyageId}}","vesselId":"{{VesselId}}",
        "portId":"50000000-0000-0000-0000-000000000005","voyageNumber":"V-2026-014","stopOrder":2,
        "portName":"Rotterdam","portUnLocode":"NLRTM","berthName":"Euromax 3","usesLiveValues":false,
        "actualArrivalUtc":"2026-09-02T20:00:00Z","actualDepartureUtc":"2026-09-03T08:00:00Z",
        "statementAvailability":"Finalized","statementId":"60000000-0000-0000-0000-000000000006",
        "finalizedAtUtc":"2026-09-04T09:12:00Z",
        "turnaround":{{Metric(720)}},
        "arrivalToAllFast":{{Metric(null, "Missing", "AllFast")}},
        "cargoElapsed":{{Metric(null, "Ambiguous", "CargoStarted")}},
        "cargoCompletedToDeparture":{{Metric(null, "InvalidChronology")}},
        "recordedDelayCount":1,"recordedDelayMinutes":45,"recordedDelayHours":0.75}
        """;

    private static string NotFinalizedRow() =>
        $$"""
        {"voyageStopId":"{{NotFinalizedStopId}}","voyageId":"30000000-0000-0000-0000-000000000009","vesselId":"{{VesselId}}",
        "portId":"50000000-0000-0000-0000-000000000005","voyageNumber":"V-2026-023","stopOrder":1,
        "portName":"Rotterdam","portUnLocode":"NLRTM","berthName":null,"usesLiveValues":true,
        "actualArrivalUtc":"2026-09-17T20:00:00Z","actualDepartureUtc":"2026-09-18T06:00:00Z",
        "statementAvailability":"NotFinalized","statementId":null,"finalizedAtUtc":null,
        "turnaround":null,"arrivalToAllFast":null,"cargoElapsed":null,"cargoCompletedToDeparture":null,
        "recordedDelayCount":null,"recordedDelayMinutes":null,"recordedDelayHours":null}
        """;

    private static string MetricSummary(string? meanHours, int samples, int missing = 0, int ambiguous = 0, int invalid = 0)
    {
        var mean = meanHours ?? "null";

        return $$"""{"meanMinutes":null,"meanHours":{{mean}},"sampleCount":{{samples}},"missingCount":{{missing}},"ambiguousCount":{{ambiguous}},"invalidChronologyCount":{{invalid}}}""";
    }

    private static string Summary(int stops) =>
        stops == 0
            ? $$"""
                {"stopCount":0,"finalizedCount":0,"notFinalizedCount":0,
                "turnaround":{{MetricSummary(null, 0)}},"arrivalToAllFast":{{MetricSummary(null, 0)}},
                "cargoElapsed":{{MetricSummary(null, 0)}},"cargoCompletedToDeparture":{{MetricSummary(null, 0)}},
                "recordedDelayCount":0,"recordedDelayMinutes":0,"recordedDelayHours":0.00}
                """
            : $$"""
                {"stopCount":2,"finalizedCount":1,"notFinalizedCount":1,
                "turnaround":{{MetricSummary("12.00", 1)}},"arrivalToAllFast":{{MetricSummary(null, 0, missing: 1)}},
                "cargoElapsed":{{MetricSummary(null, 0, ambiguous: 1)}},"cargoCompletedToDeparture":{{MetricSummary(null, 0, invalid: 1)}},
                "recordedDelayCount":1,"recordedDelayMinutes":45,"recordedDelayHours":0.75}
                """;

    private static string Page(int totalCount, params string[] rows)
    {
        var summary = Summary(rows.Length);
        var ports = rows.Length == 0
            ? "[]"
            : $$"""[{"portId":"50000000-0000-0000-0000-000000000005","portName":"Rotterdam","portUnLocode":"NLRTM","summary":{{summary}}}]""";

        return $$"""
            {"departedFrom":"2026-09-01","departedTo":"2026-09-30","items":[{{string.Join(',', rows)}}],
            "pageNumber":1,"pageSize":25,"totalCount":{{totalCount}},"summary":{{summary}},"ports":{{ports}}}
            """;
    }

    private static string PortSummary(string portId, string portName, int stops, string? turnaroundHours, int delayMinutes)
    {
        var turnaround = turnaroundHours is null
            ? MetricSummary(null, 0, missing: stops)
            : $$"""{"meanMinutes":{{(decimal.Parse(turnaroundHours, CultureInfo.InvariantCulture) * 60).ToString(CultureInfo.InvariantCulture)}},"meanHours":{{turnaroundHours}},"sampleCount":{{stops}},"missingCount":0,"ambiguousCount":0,"invalidChronologyCount":0}""";
        var delayHours = (delayMinutes / 60m).ToString("0.00", CultureInfo.InvariantCulture);

        return $$"""
            {"portId":"{{portId}}","portName":"{{portName}}","portUnLocode":null,"summary":
            {"stopCount":{{stops}},"finalizedCount":{{stops}},"notFinalizedCount":0,
            "turnaround":{{turnaround}},"arrivalToAllFast":{{MetricSummary(null, 0)}},
            "cargoElapsed":{{MetricSummary(null, 0)}},"cargoCompletedToDeparture":{{MetricSummary(null, 0)}},
            "recordedDelayCount":{{(delayMinutes > 0 ? 1 : 0)}},"recordedDelayMinutes":{{delayMinutes}},"recordedDelayHours":{{delayHours}} } }
            """;
    }

    private static string PageWithPorts(params string[] ports) =>
        $$"""
        {"departedFrom":"2026-09-01","departedTo":"2026-09-30","items":[{{FinalizedRow()}}],
        "pageNumber":1,"pageSize":25,"totalCount":1,"summary":{{Summary(1)}},"ports":[{{string.Join(',', ports)}}]}
        """;

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
