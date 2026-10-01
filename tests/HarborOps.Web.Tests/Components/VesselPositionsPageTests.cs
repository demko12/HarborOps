using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Fleet;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarborOps.Web.Tests.Components;

public sealed class VesselPositionsPageTests : BunitContext
{
    private const string FreshVesselId = "40000000-0000-0000-0000-000000000001";
    private const string StaleVesselId = "40000000-0000-0000-0000-000000000002";
    private const string MissingVesselId = "40000000-0000-0000-0000-000000000003";
    private const string FleetId = "70000000-0000-0000-0000-000000000001";

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<(string Path, string Body)> writes = [];
    private readonly List<CancellationToken> tokens = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Positions());

    [Fact]
    public async Task Prerendering_and_disposal_do_not_call_javascript()
    {
        RenderPositionsPage();
        var initialCalls = JSInterop.Invocations.Count;
        await using (var renderer = new HtmlRenderer(Services, Services.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var page = await renderer.RenderComponentAsync<VesselPositions>();
                Assert.Contains("Vessel positions", page.ToHtmlString(), StringComparison.Ordinal);
            });
        }
        Assert.Equal(initialCalls, JSInterop.Invocations.Count);
    }

    [Fact]
    public async Task Disposing_an_initialized_map_cleans_it_up_once()
    {
        var page = RenderPositionsPage();
        await page.Instance.DisposeAsync();
        await page.Instance.DisposeAsync();
        Assert.Single(JSInterop.Invocations["fleetPositionsMap.dispose"]);
    }

    [Fact]
    public void Ais_tracking_status_and_observation_source_are_visible()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions().Replace("\"evaluatedAtUtc\":", "\"aisTracking\":{\"connectionState\":\"Connected\",\"lastPositionReceivedAtUtc\":\"2026-10-01T10:00:00Z\"},\"evaluatedAtUtc\":", StringComparison.Ordinal)
            .Replace("\"isPositionStale\":false", "\"positionSource\":\"AISStream\",\"isPositionStale\":false", StringComparison.Ordinal));
        var page = RenderPositionsPage();
        Assert.Contains("AIS feed connected", page.Find(".positions-tracking").TextContent, StringComparison.Ordinal);
        Assert.Contains("AISStream", page.Find(".positions-table").TextContent, StringComparison.Ordinal);
        Assert.True(page.Find(".positions-tracking input").HasAttribute("checked"));
    }

    [Fact]
    public async Task Automatic_refresh_updates_the_visible_data_check_time()
    {
        var evaluatedAt = "2026-08-26T12:00:00Z";
        respond = _ => Json(HttpStatusCode.OK, Positions().Replace("2026-08-26T12:00:00Z", evaluatedAt, StringComparison.Ordinal));
        var page = RenderPositionsPage();
        Assert.Contains("12:00:00 UTC", page.Find(".positions-refresh-status").TextContent, StringComparison.Ordinal);
        evaluatedAt = "2026-08-26T12:00:15Z";
        await Task.Delay(TimeSpan.FromSeconds(16));
        page.WaitForAssertion(() => Assert.Contains("12:00:15 UTC", page.Find(".positions-refresh-status").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Connected_feed_without_observations_explains_that_no_reports_have_arrived()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions().Replace("\"evaluatedAtUtc\":", "\"aisTracking\":{\"connectionState\":\"Connected\"},\"evaluatedAtUtc\":", StringComparison.Ordinal));
        var page = RenderPositionsPage();
        Assert.Contains("No AIS position reports received", page.Find(".positions-tracking").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_map_shows_known_positions_and_lists_vessels_without_coordinates()
    {
        var page = RenderPositionsPage(CurrentUserAuthorizationState.FleetReadPermission);

        Assert.NotNull(page.Find(".positions-map"));
        Assert.Contains("2 of 3", page.Find(".positions-map-card").TextContent, StringComparison.Ordinal);
        Assert.Contains("Western Gale", page.Find(".positions-map-missing").TextContent, StringComparison.Ordinal);
        var call = JSInterop.Invocations["fleetPositionsMap.render"].Last();
        var markers = Assert.IsAssignableFrom<IEnumerable<object>>(call.Arguments[2]).ToList();
        Assert.Equal(2, markers.Count);
    }

    [Fact]
    public void The_map_loads_all_pages_and_keeps_them_when_the_table_page_changes()
    {
        respond = request =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var size = int.Parse(query["pageSize"].ToString());
            var number = int.Parse(PageNumberFrom(request));
            var items = string.Join(',', Enumerable.Range((number - 1) * size, Math.Min(size, 201 - (number - 1) * size))
                .Select(index => Item($"40000000-0000-0000-0000-{index + 1:000000000000}",
                    $"Vessel {index + 1}", $"{5100000 + index}", latitude: "10", longitude: "20", isPositionStale: "false")));
            return Json(HttpStatusCode.OK, Positions(items, "201", pageNumber: number.ToString(), pageSize: size.ToString()));
        };

        var page = RenderPositionsPage();

        Assert.Contains("201 of 201", page.Find(".positions-map-card").TextContent, StringComparison.Ordinal);
        Assert.Contains("201 of 201 matching vessels", page.Find(".positions-coverage").TextContent, StringComparison.Ordinal);
        Assert.Equal(50, page.FindAll(".positions-table tbody tr").Count);
        Assert.Equal(3, PositionRequests().Count(request => QueryHelpers.ParseQuery(request.RequestUri!.Query)["pageSize"] == "100"));
        var call = JSInterop.Invocations["fleetPositionsMap.render"].Last();
        Assert.Equal(201, Assert.IsAssignableFrom<IEnumerable<object>>(call.Arguments[2]).Count());

        page.Find(".positions-pagination button:last-child").Click();
        Assert.Contains("201 of 201", page.Find(".positions-map-card").TextContent, StringComparison.Ordinal);
        Assert.Contains("201 of 201 matching vessels", page.Find(".positions-coverage").TextContent, StringComparison.Ordinal);
        Assert.Contains("Vessel 51", page.Find(".positions-table").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, PositionRequests().Count(request => QueryHelpers.ParseQuery(request.RequestUri!.Query)["pageSize"] == "100"));
    }

    [Fact]
    public void Coverage_counts_all_205_matching_vessels_including_missing_positions()
    {
        respond = request =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var size = int.Parse(query["pageSize"].ToString());
            var number = int.Parse(PageNumberFrom(request));
            var items = string.Join(',', Enumerable.Range((number - 1) * size, Math.Min(size, 205 - (number - 1) * size))
                .Select(index => Item($"40000000-0000-0000-0000-{index + 1:000000000000}",
                    $"Vessel {index + 1}", $"{5100000 + index}",
                    latitude: index < 150 ? "10" : "null", longitude: index < 150 ? "20" : "null",
                    isPositionStale: index < 50 ? "false" : "true")));
            return Json(HttpStatusCode.OK, Positions(items, "205", pageNumber: number.ToString(), pageSize: size.ToString()));
        };

        var page = RenderPositionsPage();
        Assert.Equal(50, page.FindAll(".positions-table tbody tr").Count);
        Assert.Contains("50 of 205 matching vessels", page.Find(".positions-coverage-heading").TextContent, StringComparison.Ordinal);
        var rows = page.FindAll(".positions-coverage-row");
        Assert.Equal("50", rows[0].QuerySelector("strong")!.TextContent);
        Assert.Equal("100", rows[1].QuerySelector("strong")!.TextContent);
        Assert.Equal("55", rows[2].QuerySelector("strong")!.TextContent);
        Assert.Equal("24.4%", rows[0].QuerySelector(".positions-coverage-percent")!.TextContent);
        Assert.Equal("48.8%", rows[1].QuerySelector(".positions-coverage-percent")!.TextContent);
        Assert.Equal("26.8%", rows[2].QuerySelector(".positions-coverage-percent")!.TextContent);
        Assert.Equal("55", page.FindAll(".positions-summary-card strong")[1].TextContent);
    }

    [Fact]
    public void A_failed_map_batch_does_not_display_an_incomplete_fleet_or_hide_the_table()
    {
        respond = request => QueryHelpers.ParseQuery(request.RequestUri!.Query)["pageSize"] == "100"
            ? Json(HttpStatusCode.InternalServerError, "{}")
            : Json(HttpStatusCode.OK, Positions(totalCount: "101"));

        var page = RenderPositionsPage();

        Assert.NotNull(page.Find(".positions-map-card .api-error-state"));
        Assert.Empty(page.FindAll(".positions-map"));
        Assert.Empty(page.FindAll(".positions-coverage-ring"));
        Assert.Contains("Coverage is unavailable", page.Find(".positions-coverage").TextContent, StringComparison.Ordinal);
        Assert.Equal(3, page.FindAll(".positions-table tbody tr").Count);
    }

    [Fact]
    public void Missing_map_library_offers_a_retry_and_keeps_position_data_available()
    {
        var page = RenderPositionsPage();
        JSInterop.Setup<bool>("fleetPositionsMap.render", _ => true).SetResult(false);
        page.Find(".positions-refresh").Click();

        page.WaitForAssertion(() => Assert.Contains("The map is unavailable", page.Find(".positions-map-card").TextContent, StringComparison.Ordinal));
        Assert.NotNull(page.Find(".positions-table"));
        JSInterop.Setup<bool>("fleetPositionsMap.render", _ => true).SetResult(true);
        page.Find(".positions-map-card button").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".positions-map")));
    }

    [Fact]
    public void Every_vessel_is_listed_in_the_table()
    {
        var page = RenderPositionsPage();

        var rows = page.FindAll(".positions-table tbody tr");

        Assert.Equal(3, rows.Count);
        Assert.Contains("Northern Star", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("5100001", rows[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vessel_row_opens_the_position_report_for_a_fleet_manager()
    {
        var page = RenderPositionsPage();

        page.Find($".positions-table tbody tr").Click();

        Assert.NotEmpty(page.FindAll(".positions-report-modal"));
    }

    [Fact]
    public void Coordinates_are_formatted_with_hemispheres()
    {
        var page = RenderPositionsPage();

        var row = page.FindAll(".positions-table tbody tr")[2].TextContent;

        Assert.Contains("35.8988° N", row, StringComparison.Ordinal);
        Assert.Contains("5.1235° W", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Southern_and_eastern_coordinates_carry_their_own_hemispheres()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions(items: Item(
            StaleVesselId,
            "Southern Cross",
            "5100002",
            latitude: "-1.29",
            longitude: "36.82",
            reportedAtUtc: "\"2026-08-20T10:00:00Z\"",
            isPositionStale: "true")));

        var page = RenderPositionsPage();

        var row = page.Find(".positions-table tbody tr").TextContent;

        Assert.Contains("1.2900° S", row, StringComparison.Ordinal);
        Assert.Contains("36.8200° E", row, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vessel_without_coordinates_is_marked_as_missing()
    {
        var page = RenderPositionsPage();

        var row = page.FindAll(".positions-table tbody tr")[0];

        Assert.Contains("Western Gale", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("—", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Never reported", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("No position", row.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "positions-badge-missing",
            row.QuerySelector(".positions-badge")!.ClassName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_position_keeps_its_coordinates_and_carries_the_stale_badge()
    {
        var page = RenderPositionsPage();

        var row = page.FindAll(".positions-table tbody tr")[1];

        Assert.Contains("Southern Cross", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.2900° S", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stale", row.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "positions-badge-stale",
            row.QuerySelector(".positions-badge")!.ClassName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_recent_position_is_marked_as_current()
    {
        var page = RenderPositionsPage();

        var row = page.FindAll(".positions-table tbody tr")[2];

        Assert.Contains(
            "positions-badge-current",
            row.QuerySelector(".positions-badge")!.ClassName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_stale_threshold_comes_from_the_api_rather_than_the_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions(staleAfterHours: "6"));

        var page = RenderPositionsPage();

        Assert.Contains("6 h", page.Find(".positions-card-heading").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("12 h", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_counts_missing_stale_and_current_positions()
    {
        var page = RenderPositionsPage();

        var cards = page.FindAll(".positions-summary-card");

        Assert.Contains("3", cards[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("1", cards[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("1", cards[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("1", cards[3].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overview_lists_only_the_vessels_that_need_a_report()
    {
        var page = RenderPositionsPage();

        var cards = page.FindAll(".positions-overview-card");

        Assert.Equal(2, cards.Count);
        Assert.Contains("Western Gale", cards[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Southern Cross", cards[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Northern Star",
            page.Find(".positions-overview").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_attention_card_links_to_report_a_defect_with_the_maintenance_manage_permission()
    {
        var page = RenderPositionsPage("maintenance.manage");

        var card = page.FindAll(".positions-overview-card")
            .Single(item => item.TextContent.Contains("Western Gale", StringComparison.Ordinal));

        Assert.Equal(
            $"/maintenance/defects/report?vesselId={MissingVesselId}",
            card.QuerySelector("a[href*='defects/report']")!.GetAttribute("href"));
    }

    [Fact]
    public void An_attention_card_offers_no_defect_link_without_the_maintenance_manage_permission()
    {
        var page = RenderPositionsPage(CurrentUserAuthorizationState.FleetManagePermission);

        var card = page.FindAll(".positions-overview-card")
            .Single(item => item.TextContent.Contains("Western Gale", StringComparison.Ordinal));

        Assert.Null(card.QuerySelector("a[href*='defects/report']"));
    }

    [Fact]
    public void The_overview_confirms_when_every_position_is_current()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions(items: Item(
            FreshVesselId,
            "Northern Star",
            "5100001",
            latitude: "35.898765",
            longitude: "-5.123456",
            reportedAtUtc: "\"2026-08-26T11:00:00Z\"",
            isPositionStale: "false")));

        var page = RenderPositionsPage();

        Assert.Empty(page.FindAll(".positions-overview-card"));
        Assert.Contains(
            "Every vessel on this page reported a position",
            page.Find(".positions-overview-clear").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Filtering_by_fleet_sends_the_fleet_to_the_api()
    {
        var page = RenderPositionsPage();

        page.Find("#positions-fleet").Change(FleetId);

        page.WaitForAssertion(() => Assert.Contains(
            $"fleetId={FleetId}",
            LastPositionsQuery(),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Filtering_by_status_sends_the_status_to_the_api()
    {
        var page = RenderPositionsPage();

        page.Find("#positions-status").Change("At Port");

        page.WaitForAssertion(() => Assert.Contains(
            "status=At%20Port",
            LastPositionsQuery(),
            StringComparison.Ordinal));
    }

    [Fact]
    public void Asking_for_stale_positions_only_sends_the_flag_to_the_api()
    {
        var page = RenderPositionsPage();

        page.Find("#positions-stale-only").Change(true);

        page.WaitForAssertion(() => Assert.Contains(
            "staleOnly=true",
            LastPositionsQuery(),
            StringComparison.Ordinal));
    }

    [Fact]
    public void Resetting_clears_every_filter()
    {
        var page = RenderPositionsPage();

        page.Find("#positions-fleet").Change(FleetId);
        page.Find("#positions-stale-only").Change(true);
        page.WaitForAssertion(() => Assert.Contains("staleOnly=true", LastPositionsQuery(), StringComparison.Ordinal));

        page.Find(".positions-reset").Click();

        page.WaitForAssertion(() =>
        {
            var query = LastPositionsQuery();

            Assert.DoesNotContain("fleetId=", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("status=", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("staleOnly=", query, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void A_filter_change_returns_to_the_first_page()
    {
        respond = request => Json(HttpStatusCode.OK, Positions(
            totalCount: "120",
            pageNumber: PageNumberFrom(request)));

        var page = RenderPositionsPage();

        page.Find(".positions-pagination button:last-child").Click();

        page.WaitForAssertion(() => Assert.Contains("pageNumber=2", LastPositionsQuery(), StringComparison.Ordinal));

        page.Find("#positions-status").Change("At Port");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("pageNumber=1", LastPositionsQuery(), StringComparison.Ordinal);
            Assert.Contains("status=At%20Port", LastPositionsQuery(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Refreshing_reads_the_positions_again()
    {
        var page = RenderPositionsPage();
        var readsBefore = PositionReadCount();

        page.Find(".positions-refresh").Click();

        page.WaitForAssertion(() => Assert.True(PositionReadCount() > readsBefore));
    }

    [Fact]
    public void An_empty_result_offers_the_empty_state_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Positions(items: string.Empty, totalCount: "0"));

        var page = RenderPositionsPage();

        Assert.Empty(page.FindAll(".positions-table"));
        Assert.Contains("No vessels found", page.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forbidden_read_explains_the_missing_permission_without_offering_a_retry()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPositionsPage();

        Assert.Contains("Fleet access denied", page.Find(".positions-alert").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_failed_read_reports_the_problem_from_the_response()
    {
        respond = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load vessel positions","detail":"The register is offline.","status":500}""");

        var page = RenderPositionsPage();

        var error = page.Find(".api-error-state").TextContent;

        Assert.Contains("Unable to load vessel positions", error, StringComparison.Ordinal);
        Assert.Contains("The register is offline.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_api_is_reported_as_unavailable()
    {
        respond = _ => throw new HttpRequestException();

        var page = RenderPositionsPage();

        Assert.Contains(
            "HarborOps API is unavailable",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_after_a_failed_read_requests_the_positions_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderPositionsPage();
        var readsBefore = PositionReadCount();

        respond = _ => Json(HttpStatusCode.OK, Positions());

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(PositionReadCount() > readsBefore);
            Assert.Contains("Northern Star", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_rejected_filter_is_listed_as_a_validation_problem()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status must be one of: Active, En Route, At Port, Maintenance, Inactive."]}}""");

        var page = RenderPositionsPage();

        Assert.Contains(
            "Status must be one of",
            page.Find(".positions-alert").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_form_is_withheld_from_a_read_only_operator()
    {
        var page = RenderPositionsPage(permissions: CurrentUserAuthorizationState.FleetReadPermission);

        Assert.Empty(page.FindAll("form.positions-form"));
        Assert.Empty(page.FindAll(".positions-form-card"));
        Assert.NotEmpty(page.FindAll(".positions-table"));
    }

    [Fact]
    public void A_fleet_manager_can_open_a_report_modal_from_a_vessel_row()
    {
        var page = RenderPositionsPage(permissions: CurrentUserAuthorizationState.FleetManagePermission);

        page.Find(".positions-table tbody tr").Click();
        Assert.NotNull(page.Find("form.positions-form"));
        Assert.NotNull(page.Find(".positions-report-modal"));
    }

    [Fact]
    public void The_report_form_is_not_visible_until_a_vessel_is_chosen()
    {
        var page = RenderPositionsPage();

        Assert.Empty(page.FindAll(".positions-report-modal"));
    }

    [Fact]
    public void Submitting_posts_the_coordinates_to_the_chosen_vessel()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.OK, PositionAccepted());

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10.5");
        page.Find("#positions-longitude").Change("20.25");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);

            Assert.Equal($"/api/fleet/vessels/{MissingVesselId}/position-reports", write.Path);
            Assert.Contains("\"latitude\":10.5", write.Body, StringComparison.Ordinal);
            Assert.Contains("\"longitude\":20.25", write.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Submitting_never_sends_an_observation_time()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.OK, PositionAccepted());

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10.5");
        page.Find("#positions-longitude").Change("20.25");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() => Assert.DoesNotContain(
            "reportedAt",
            Assert.Single(writes).Body,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_recorded_position_is_confirmed_and_the_list_is_read_again()
    {
        var reads = 0;
        respond = request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, PositionAccepted());
            }

            reads++;
            return Json(HttpStatusCode.OK, reads <= 1
                ? Positions()
                : Positions(items: Item(
                    MissingVesselId,
                    "Western Gale",
                    "5100003",
                    latitude: "10.5",
                    longitude: "20.25",
                    reportedAtUtc: "\"2026-08-26T12:00:00Z\"",
                    isPositionStale: "false")));
        };

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10.5");
        page.Find("#positions-longitude").Change("20.25");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() =>
        {
            var success = page.Find(".positions-success").TextContent;

            Assert.Contains("Position recorded for Western Gale", success, StringComparison.Ordinal);
            Assert.Contains("10.5000° N", success, StringComparison.Ordinal);
            Assert.Contains("20.2500° E", success, StringComparison.Ordinal);
            Assert.True(reads >= 2);
            Assert.Contains("10.5000° N", page.Find(".positions-table").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_recorded_position_clears_the_stale_only_filter_so_the_vessel_remains_visible()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions(items: Item(
                MissingVesselId,
                "Western Gale",
                "5100003",
                latitude: "10.5",
                longitude: "20.25",
                reportedAtUtc: "\"2026-08-26T12:00:00Z\"",
                isPositionStale: "false")))
            : Json(HttpStatusCode.OK, PositionAccepted());

        var page = RenderPositionsPage();
        page.Find("#positions-stale-only").Change(true);
        page.WaitForAssertion(() => Assert.Contains("staleOnly=true", LastPositionsQuery(), StringComparison.Ordinal));

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10.5");
        page.Find("#positions-longitude").Change("20.25");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("staleOnly=true", LastPositionsQuery(), StringComparison.Ordinal);
            Assert.Contains("Western Gale", page.Find(".positions-table").TextContent, StringComparison.Ordinal);
            Assert.Contains("Current", page.Find(".positions-table").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_coordinate_fields_are_cleared_once_the_report_is_accepted()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.OK, PositionAccepted());

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10.5");
        page.Find("#positions-longitude").Change("20.25");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".positions-success"));
            Assert.Empty(page.FindAll(".positions-report-modal"));
        });
    }

    [Fact]
    public void Coordinates_rejected_by_the_api_are_listed_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"latitude":["Latitude must be between -90 and 90."]}}""");

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("120");
        page.Find("#positions-longitude").Change("20");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Latitude must be between -90 and 90.",
            page.Find(".positions-report-modal .positions-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_report_refused_by_the_api_explains_the_missing_permission()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10");
        page.Find("#positions-longitude").Change("20");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Your account does not have permission for this action.",
            page.Find(".positions-report-modal .positions-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_vessel_that_disappears_mid_report_is_reported_as_missing()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.NotFound, """{"title":"Vessel not found.","status":404}""");

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10");
        page.Find("#positions-longitude").Change("20");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Vessel not found",
            page.Find(".positions-report-modal .positions-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_report_can_be_retried()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderPositionsPage();

        SelectReportVessel(page);
        page.Find("#positions-latitude").Change("10");
        page.Find("#positions-longitude").Change("20");
        page.Find("form.positions-form").Submit();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".positions-report-modal .api-error-state")));

        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Positions())
            : Json(HttpStatusCode.OK, PositionAccepted());

        page.Find(".positions-report-modal .api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, writes.Count);
            Assert.NotEmpty(page.FindAll(".positions-success"));
        });
    }

    [Fact]
    public void The_page_reports_that_it_is_loading_until_the_api_answers()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>();
        respond = request => request.Method == HttpMethod.Get
                             && request.RequestUri!.AbsolutePath == "/api/fleet/vessel-positions"
            ? release.Task
            : Json(HttpStatusCode.OK, Fleets());

        var page = RenderPositionsPage();

        Assert.NotEmpty(page.FindAll(".loading-state"));

        release.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Positions(), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".loading-state")));
    }

    [Fact]
    public async Task Leaving_the_page_cancels_the_request_that_is_still_in_flight()
    {
        var page = RenderPositionsPage();

        var release = new TaskCompletionSource<HttpResponseMessage>();
        respond = _ => release.Task;

        page.Find(".positions-refresh").Click();
        page.WaitForAssertion(() => Assert.Equal(2, PositionReadCount()));

        var inFlightToken = tokens[^1];
        Assert.False(inFlightToken.IsCancellationRequested);

        await DisposeComponentsAsync();

        Assert.True(inFlightToken.IsCancellationRequested);

        release.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Positions(), Encoding.UTF8, "application/json"),
        });
    }

    private IRenderedComponent<VesselPositions> RenderPositionsPage(params string[] permissions)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("fleetPositionsMap.render", _ => true).SetResult(true);

        var httpClient = new HttpClient(new StubHttpMessageHandler((request, token) =>
        {
            requests.Add(request);
            tokens.Add(token);

            if (request.RequestUri!.AbsolutePath == "/api/fleet/vessels")
            {
                return Json(HttpStatusCode.OK, VesselSearchResults());
            }

            if (request.Method != HttpMethod.Get)
            {
                writes.Add((
                    request.RequestUri!.AbsolutePath,
                    request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            }

            return request.RequestUri!.AbsolutePath == "/api/fleet/fleets"
                ? Json(HttpStatusCode.OK, Fleets())
                : respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new FleetApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? [CurrentUserAuthorizationState.FleetManagePermission] : permissions));

        var page = Render<VesselPositions>();

        page.WaitForAssertion(() => Assert.NotEmpty(PositionRequests()));

        return page;
    }

    private static void SelectReportVessel(IRenderedComponent<VesselPositions> page)
    {
        page.FindAll(".positions-table tbody tr")
            .Single(row => row.TextContent.Contains("Western Gale", StringComparison.Ordinal))
            .Click();
    }

    private List<HttpRequestMessage> PositionRequests() =>
        requests.FindAll(request => request.RequestUri!.AbsolutePath == "/api/fleet/vessel-positions");

    private int PositionReadCount() => PositionRequests().Count;

    private string LastPositionsQuery() => PositionRequests().Last(request => QueryHelpers.ParseQuery(request.RequestUri!.Query)["pageSize"] == "50").RequestUri!.Query;

    private static CurrentUserAuthorizationState CreateAuthorizationState(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private static string Item(
        string vesselId,
        string name,
        string imoNumber,
        string latitude = "null",
        string longitude = "null",
        string reportedAtUtc = "null",
        string isPositionStale = "true",
        string currentStatus = "Active",
        string fleetId = "null",
        string fleetName = "null")
    {
        return $$"""
            {"vesselId":"{{vesselId}}","name":"{{name}}","imoNumber":"{{imoNumber}}",
            "currentStatus":"{{currentStatus}}","fleetId":{{fleetId}},"fleetName":{{fleetName}},
            "currentLatitude":{{latitude}},"currentLongitude":{{longitude}},
            "lastPositionReportedAtUtc":{{reportedAtUtc}},"isPositionStale":{{isPositionStale}}}
            """;
    }

    // Ordered the way the API returns it: missing and stale first, then the current position.
    private static string Positions(
        string? items = null,
        string totalCount = "3",
        string staleAfterHours = "12",
        string pageNumber = "1",
        string pageSize = "50")
    {
        items ??= string.Join(
            ',',
            Item(MissingVesselId, "Western Gale", "5100003"),
            Item(
                StaleVesselId,
                "Southern Cross",
                "5100002",
                latitude: "-1.29",
                longitude: "36.82",
                reportedAtUtc: "\"2026-08-20T10:00:00Z\"",
                isPositionStale: "true"),
            Item(
                FreshVesselId,
                "Northern Star",
                "5100001",
                latitude: "35.898765",
                longitude: "-5.123456",
                reportedAtUtc: "\"2026-08-26T11:00:00Z\"",
                isPositionStale: "false",
                fleetId: $"\"{FleetId}\"",
                fleetName: "\"Adriatic Fleet\""));

        return $$"""
            {"items":[{{items}}],"pageNumber":{{pageNumber}},"pageSize":{{pageSize}},"totalCount":{{totalCount}},
            "staleAfterHours":{{staleAfterHours}},"evaluatedAtUtc":"2026-08-26T12:00:00Z"}
            """;
    }

    private static string Fleets()
    {
        return $$"""
            {"items":[{"fleetId":"{{FleetId}}","name":"Adriatic Fleet","description":"Adriatic vessels",
            "createdAtUtc":"2026-01-01T08:00:00Z","activeVesselCount":2}],
            "totalCount":1,"pageNumber":1,"pageSize":100}
            """;
    }

    private static string VesselSearchResults()
    {
        return $$"""
            {"items":[{"vesselId":"{{MissingVesselId}}","name":"Western Gale","imoNumber":"5100003","currentStatus":"Active","fleetName":null,"flagState":"Panama","vesselTypeName":"Container Ship"}],"pageNumber":1,"pageSize":10,"totalCount":1}
            """;
    }

    private static string PositionAccepted()
    {
        return $$"""
            {"vesselId":"{{MissingVesselId}}","latitude":10.5,"longitude":20.25,
            "reportedAtUtc":"2026-08-26T12:00:00Z"}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }

    private static string PageNumberFrom(HttpRequestMessage request) =>
    QueryHelpers.ParseQuery(request.RequestUri!.Query).TryGetValue("pageNumber", out var value)
        ? value.ToString()
        : "1";
}
