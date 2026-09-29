using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class VoyagesPageTests : BunitContext
{
    private readonly List<Uri> requestedUris = [];
    private readonly List<(string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page());

    private string? startUri;

    [Fact]
    public void Rows_render_the_route_and_utc_stamps()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0001")));

        var page = RenderVoyagesPage();

        Assert.Equal("MEBAR → ITGOA", page.Find("td.voyage-route").TextContent.Trim());
        Assert.Contains("15 Sep 2026 08:00", page.Markup, StringComparison.Ordinal);
        Assert.Contains("18 Sep 2026 17:30", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_prefer_itinerary_stop_port_names_over_the_scheduled_codes()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            Page(Row("VOY-0002", firstStopPortName: "Port of Bar", lastStopPortName: "Port of Dubrovnik")));

        var page = RenderVoyagesPage();

        Assert.Equal("Port of Bar → Port of Dubrovnik", page.Find("td.voyage-route").TextContent.Trim());
    }

    [Fact]
    public void Missing_actual_dates_render_as_a_placeholder_instead_of_breaking_the_row()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0002")));

        var page = RenderVoyagesPage();

        var actualCell = page.FindAll("td.voyage-stamps")[1];

        Assert.Equal(2, actualCell.QuerySelectorAll("span.missing").Length);
        Assert.Contains(Voyages.MissingValue, actualCell.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Departures_inside_the_next_day_are_marked_on_the_row()
    {
        var imminent = DateTime.UtcNow.AddHours(2);

        respond = _ => Json(HttpStatusCode.OK, Page(
            Row("VOY-0003", plannedDeparture: Stamp(imminent)),
            Row("VOY-0004")));

        var page = RenderVoyagesPage();

        var markedRow = Assert.Single(page.FindAll("tbody tr.imminent"));

        Assert.Contains("VOY-0003", markedRow.TextContent, StringComparison.Ordinal);
        Assert.Equal("Departs within 24 hours", markedRow.GetAttribute("title"));
    }

    [Fact]
    public void The_status_column_shows_the_position_in_the_voyage_lifecycle()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0005", status: "InProgress")));

        var page = RenderVoyagesPage();

        Assert.Equal(3, page.FindAll(".voyage-steps i").Count);
        Assert.Equal(2, page.FindAll(".voyage-steps i.on").Count);
        Assert.Equal("InProgress", page.Find(".voyage-status").TextContent.Trim());
    }

    [Fact]
    public void The_board_opens_in_the_list_view()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0301")));

        var page = RenderVoyagesPage();

        Assert.Equal("true", page.Find(".voyages-view-switch button[aria-label='List view']").GetAttribute("aria-pressed"));
        Assert.Single(page.FindAll("tbody tr"));
        Assert.Empty(page.FindAll(".voyage-card"));
    }

    [Fact]
    public void The_card_view_shows_each_voyage_with_its_route_stops_and_map()
    {
        JSInterop.SetupVoid("voyageRouteMap.renderMiniMaps", _ => true);
        respond = _ => Json(HttpStatusCode.OK, Page(Row(
            "VOY-0302",
            firstStopPortName: "Port of Bar",
            lastStopPortName: "Port of Dubrovnik",
            stops: MappedStops())));

        var page = RenderVoyagesPage();
        page.Find(".voyages-view-switch button[aria-label='Card view']").Click();

        var card = page.Find(".voyage-card");

        Assert.Empty(page.FindAll("table.voyages-table"));
        Assert.Contains("VOY-0302", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("Port of Bar 3 stops Port of Dubrovnik", NormalizeWhitespace(card.QuerySelector(".voyage-card-route")!.TextContent));
        Assert.Equal("Route map, 2 port calls", card.QuerySelector(".voyage-card-map")!.GetAttribute("aria-label"));
        Assert.Contains("\"portName\":\"Port of Dubrovnik\"", card.QuerySelector(".voyage-card-map")!.GetAttribute("data-route-stops"), StringComparison.Ordinal);
        Assert.DoesNotContain("Port of Kotor", card.QuerySelector(".voyage-card-map")!.GetAttribute("data-route-stops"), StringComparison.Ordinal);
        page.WaitForAssertion(() => JSInterop.VerifyInvoke("voyageRouteMap.renderMiniMaps"));
    }

    [Fact]
    public void Cards_without_two_mapped_ports_show_a_placeholder_instead_of_a_map()
    {
        JSInterop.SetupVoid("voyageRouteMap.renderMiniMaps", _ => true);
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0303")));

        var page = RenderVoyagesPage();
        page.Find(".voyages-view-switch button[aria-label='Card view']").Click();

        Assert.Equal("MEBAR ITGOA", NormalizeWhitespace(page.Find(".voyage-card-route").TextContent));
        Assert.Contains("Route not mapped yet", page.Find(".voyage-card-map-empty").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-route-stops]"));
    }

    [Fact]
    public void Cards_offer_the_same_actions_as_rows()
    {
        JSInterop.SetupVoid("voyageRouteMap.renderMiniMaps", _ => true);
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0304")));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);
        page.Find(".voyages-view-switch button[aria-label='Card view']").Click();

        Assert.Equal(
            ["Depart", "Cancel"],
            page.FindAll(".voyage-card .voyages-row-action").Select(button => button.TextContent.Trim()));

        page.Find(".voyage-card .voyages-row-action").Click();

        Assert.NotNull(page.Find(".voyage-card .voyage-action-form"));
    }

    [Fact]
    public void The_card_view_is_kept_when_returning_from_a_timeline()
    {
        JSInterop.SetupVoid("voyageRouteMap.renderMiniMaps", _ => true);
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0305")));

        var page = RenderVoyagesPage();
        page.Find(".voyages-view-switch button[aria-label='Card view']").Click();

        Assert.EndsWith("?from=view%3Dcards", page.Find(".voyage-card-open").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_view_is_restored_from_the_board_query()
    {
        JSInterop.SetupVoid("voyageRouteMap.renderMiniMaps", _ => true);
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0306")));
        startUri = "/voyages?view=cards";

        var page = RenderVoyagesPage();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".voyage-card")));
        Assert.Equal("true", page.Find(".voyages-view-switch button[aria-label='Card view']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Summary_cards_show_a_count_for_each_board_metric()
    {
        respond = request =>
        {
            var query = request.RequestUri!.Query;
            var total = 22;

            if (query.Contains("delayImpactApplied=true", StringComparison.Ordinal))
            {
                total = 5;
            }
            else if (query.Contains("status=Planned", StringComparison.Ordinal))
            {
                total = 7;
            }
            else if (query.Contains("status=InProgress", StringComparison.Ordinal))
            {
                total = 3;
            }
            else if (query.Contains("status=Completed", StringComparison.Ordinal))
            {
                total = 12;
            }

            return Json(HttpStatusCode.OK, Page(1, total, Row("VOY-0100")));
        };

        var page = RenderVoyagesPage();

        page.WaitForAssertion(() =>
        {
            var values = page.FindAll(".voyages-kpi .voyages-kpi-value");

            Assert.Equal(4, values.Count);
            Assert.Equal("7", values[0].TextContent.Trim());
            Assert.Equal("3", values[1].TextContent.Trim());
            Assert.Equal("12", values[2].TextContent.Trim());
            Assert.Equal("5", values[3].TextContent.Trim());
        });
    }

    [Fact]
    public void Clicking_a_status_summary_card_filters_the_board_and_clicking_it_again_clears_the_filter()
    {
        var page = RenderVoyagesPage();
        var inProgress = "button.voyages-kpi[aria-label='Show voyages in progress']";

        page.Find(inProgress).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("status=InProgress", ResultQueries()[^1], StringComparison.Ordinal);
            Assert.Equal("true", page.Find(inProgress).GetAttribute("aria-pressed"));
            Assert.Contains("selected", page.Find(inProgress).ClassName, StringComparison.Ordinal);
            Assert.Equal(Voyages.InProgressStatus, page.Find("#voyages-status").GetAttribute("value"));
        });

        page.Find(inProgress).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("status=&", ResultQueries()[^1], StringComparison.Ordinal);
            Assert.Equal("false", page.Find(inProgress).GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void Clicking_the_delayed_summary_card_shows_only_delayed_voyages_until_the_filters_are_reset()
    {
        var page = RenderVoyagesPage();
        var delayed = "button.voyages-kpi[aria-label='Show delayed voyages']";

        page.Find(delayed).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("delayImpactApplied=true", ResultQueries()[^1], StringComparison.Ordinal);
            Assert.Equal("true", page.Find(delayed).GetAttribute("aria-pressed"));
        });

        page.Find(".voyages-reset").Click();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("delayImpactApplied", ResultQueries()[^1], StringComparison.Ordinal);
            Assert.Equal("false", page.Find(delayed).GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void The_delayed_filter_is_kept_when_returning_from_a_timeline()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0307")));
        startUri = "/voyages?delayed=true";

        var page = RenderVoyagesPage();

        page.WaitForAssertion(() => Assert.Contains("delayImpactApplied=true", ResultQueries()[^1], StringComparison.Ordinal));
        Assert.EndsWith("?from=delayed%3Dtrue", page.Find("td.voyage-number a").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Departures_inside_the_next_day_show_how_soon_they_leave()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row("VOY-0310", plannedDeparture: Stamp(DateTime.UtcNow.AddHours(5).AddMinutes(20))),
            Row("VOY-0311")));

        var page = RenderVoyagesPage();

        var label = Assert.Single(page.FindAll(".voyage-departs-in"));
        Assert.Equal("Departs in 5h", label.TextContent.Trim());
        Assert.Contains("VOY-0310", page.Find("tbody tr.imminent").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(330, "Departs in 5h")]
    [InlineData(45, "Departs in 45m")]
    [InlineData(0.5, "Departs in 1m")]
    public void Departure_countdown_uses_hours_or_minutes(double minutes, string expected)
    {
        var utcNow = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, Voyages.FormatDepartsIn(utcNow.AddMinutes(minutes), utcNow));
    }

    [Fact]
    public void A_success_message_disappears_after_a_few_seconds()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0312")))
            : Json(HttpStatusCode.OK, """{"id":"20000000-0000-0000-0000-000000000001","status":"InProgress"}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Contains("VOY-0312 is now InProgress", page.Find(".voyages-action-success").TextContent, StringComparison.Ordinal));
        page.WaitForAssertion(
            () => Assert.Empty(page.FindAll(".voyages-action-success")),
            TimeSpan.FromMilliseconds(Voyages.SuccessToastMilliseconds + 2000));
    }

    [Fact]
    public void Applying_filters_sends_the_window_status_and_page_size_to_the_api()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-from").Change("2026-09-01");
        page.Find("#voyages-to").Change("2026-09-30");
        page.Find("#voyages-status").Change("Planned");
        page.Find("#voyages-page-size").Change("50");

        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("from=2026-09-01", StringComparison.Ordinal)
                && query.Contains("to=2026-09-30", StringComparison.Ordinal)
                && query.Contains("status=Planned", StringComparison.Ordinal)
                && query.Contains("pageSize=50", StringComparison.Ordinal)));
    }

    [Fact]
    public void Searching_sends_the_voyage_number_to_the_api()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-search").Input("VOY-06");

        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("search=VOY-06", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resetting_the_filters_returns_to_the_default_window_and_first_page()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-status").Change("Completed");
        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("status=Completed", StringComparison.Ordinal)));

        page.Find(".voyages-reset").Click();

        var defaultFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultTo = defaultFrom.AddDays(Voyages.DefaultWindowDays);

        page.WaitForAssertion(() =>
        {
            var lastQuery = ResultQueries()[^1];

            Assert.Contains("status=&", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            Assert.Contains($"from={defaultFrom:yyyy-MM-dd}", lastQuery, StringComparison.Ordinal);
            Assert.Contains($"to={defaultTo:yyyy-MM-dd}", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderVoyagesPage();

        Assert.Empty(page.FindAll("table.voyages-table"));
        Assert.Contains("No voyages in this window", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respond = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderVoyagesPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.voyages-table"));
        Assert.NotNull(page.Find(".api-error-state button"));
    }

    [Fact]
    public void Retry_reloads_the_voyages_after_a_failure()
    {
        var attempt = 0;
        respond = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, Page(Row("VOY-0007")));
        };

        var page = RenderVoyagesPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("VOY-0007", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_forbidden_response_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}""");

        var page = RenderVoyagesPage();

        Assert.Contains(Voyages.ForbiddenTitle, page.Markup, StringComparison.Ordinal);
        Assert.Contains(Voyages.ForbiddenMessage, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_problems_are_listed_above_the_results()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"to":["To date must not be earlier than the from date."]}}""");

        var page = RenderVoyagesPage();

        Assert.Contains(
            "To date must not be earlier than the from date.",
            page.Find(".voyages-validation").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.voyages-table"));
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respond = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(pageNumber, 60, Row($"VOY-PAGE-{pageNumber}")));
        };

        var page = RenderVoyagesPage();

        var previous = page.FindAll(".voyages-pagination button")[0];

        Assert.True(previous.HasAttribute("disabled"));
        Assert.Contains("Page 1 of 3", page.Markup, StringComparison.Ordinal);

        page.FindAll(".voyages-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("VOY-PAGE-2", page.Markup, StringComparison.Ordinal);
            Assert.Contains(
                ResultQueries(),
                query => query.Contains("pageNumber=2", StringComparison.Ordinal));
            Assert.False(page.FindAll(".voyages-pagination button")[0].HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Without_the_manage_permission_the_board_offers_no_actions()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0200")));

        var page = RenderVoyagesPage();

        Assert.Empty(page.FindAll(".voyages-schedule-button"));
        Assert.Empty(page.FindAll(".voyages-row-action"));
        Assert.DoesNotContain("Actions", page.Find("thead").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_manage_permission_the_board_offers_scheduling()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0201")));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        Assert.Equal("/voyages/schedule", page.Find(".voyages-schedule-button").GetAttribute("href"));
        Assert.Contains("Actions", page.Find("thead").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Voyages.PlannedStatus, "Depart")]
    [InlineData(Voyages.InProgressStatus, "Arrive")]
    public void Only_the_action_valid_for_the_row_status_is_offered(string status, string expected)
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0202", status: status)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        Assert.Equal(expected, page.Find(".voyages-row-action").TextContent.Trim());
    }

    [Fact]
    public void Completed_voyages_offer_no_action()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0203", status: Voyages.CompletedStatus)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        Assert.Empty(page.FindAll(".voyages-row-action"));
        Assert.Equal(Voyages.MissingValue, page.Find(".voyage-actions-none").TextContent.Trim());
    }

    [Fact]
    public void Cancelled_voyages_render_their_own_status_badge_and_offer_no_action()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0211", status: Voyages.CancelledStatus)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        Assert.Contains("cancelled", page.Find(".voyage-status").ClassList, StringComparer.Ordinal);
        Assert.Empty(page.FindAll(".voyages-row-action"));
        Assert.Equal(Voyages.MissingValue, page.Find(".voyage-actions-none").TextContent.Trim());
    }

    [Fact]
    public void The_status_filter_offers_the_cancelled_status()
    {
        var page = RenderVoyagesPage();

        var options = page.FindAll("#voyages-status option").Select(option => option.TextContent.Trim());

        Assert.Contains(Voyages.CancelledStatus, options, StringComparer.Ordinal);
    }

    [Fact]
    public void Filtering_by_the_cancelled_status_sends_it_to_the_api()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-status").Change(Voyages.CancelledStatus);

        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("status=Cancelled", StringComparison.Ordinal)));
    }

    [Fact]
    public void Planned_voyages_offer_both_a_departure_and_a_cancellation_action()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0212", status: Voyages.PlannedStatus)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        var labels = page.FindAll(".voyages-row-action").Select(button => button.TextContent.Trim());

        Assert.Equal(["Depart", "Cancel"], labels);
    }

    [Fact]
    public void The_cancellation_action_is_hidden_from_read_only_users()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0213", status: Voyages.PlannedStatus)));

        var page = RenderVoyagesPage();

        Assert.Empty(page.FindAll(".voyages-row-action-danger"));
    }

    [Fact]
    public void Cancelling_a_voyage_asks_for_a_reason_before_confirming()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0214", status: Voyages.PlannedStatus)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action-danger").Click();

        var form = page.Find(".voyage-action-form");

        Assert.Contains("Cancel VOY-0214", form.TextContent, StringComparison.Ordinal);
        Assert.Contains("Confirm cancellation", form.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keep voyage", form.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("#voyage-action-reason"));
        Assert.Empty(page.FindAll("#voyage-action-date"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Confirming_a_cancellation_posts_the_reason_and_refreshes_the_board()
    {
        var boardRequests = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                boardRequests++;

                return Json(HttpStatusCode.OK, Page(Row("VOY-0215", status: Voyages.PlannedStatus)));
            }

            return Json(HttpStatusCode.OK, CancelledSchedule());
        };

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);
        var requestsBeforeCancel = boardRequests;

        page.Find(".voyages-row-action-danger").Click();
        page.Find("#voyage-action-reason").Change("Charter withdrawn");
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith("/cancel", StringComparison.Ordinal)
                && write.Body.Contains("\"reason\":\"Charter withdrawn\"", StringComparison.Ordinal)));
        page.WaitForAssertion(() => Assert.Contains(
            "VOY-0215 is now Cancelled",
            page.Markup,
            StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.True(boardRequests > requestsBeforeCancel));
    }

    [Fact]
    public void A_cancellation_validation_error_is_shown_and_the_form_stays_open()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0216", status: Voyages.PlannedStatus)))
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"reason":["Reason is required."]}}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action-danger").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Contains("Reason is required", page.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll("#voyage-action-reason"));
    }

    [Fact]
    public void A_cancellation_conflict_is_shown_and_the_board_is_reloaded()
    {
        var attempt = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, Page(Row("VOY-0217", status: Voyages.PlannedStatus)));
            }

            attempt++;

            return Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The voyage cannot be cancelled from its current status.","detail":"Voyage cannot change its schedule while its status is InProgress."}""");
        };

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action-danger").Click();
        page.Find("#voyage-action-reason").Change("Charter withdrawn");
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Equal(1, attempt));
        page.WaitForAssertion(() => Assert.Contains(
            "The voyage cannot be cancelled from its current status.",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Keeping_the_voyage_closes_the_cancellation_form_without_submitting()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0218", status: Voyages.PlannedStatus)));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action-danger").Click();
        page.Find(".voyage-action-buttons .voyages-reset").Click();

        Assert.Empty(page.FindAll(".voyage-action-form"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Confirming_a_departure_sends_the_utc_stamp_to_the_transition_endpoint()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0204")))
            : Json(HttpStatusCode.OK, """{"id":"20000000-0000-0000-0000-000000000001","status":"InProgress"}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();
        page.Find("#voyage-action-date").Change("2026-08-20");
        page.Find("#voyage-action-time").Change("06:30:00");
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);

            Assert.EndsWith("/departure", write.Path, StringComparison.Ordinal);
            Assert.Contains("2026-08-20T06:30:00Z", write.Body, StringComparison.Ordinal);
            Assert.Contains("is now InProgress", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_arrival_cannot_be_dated_before_its_departure()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(RowWithDeparture("VOY-0209")));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();

        var date = page.Find("#voyage-action-date");

        Assert.Equal("2026-09-15", date.GetAttribute("min"));
        Assert.Equal(
            Voyages.FormatIsoDate(DateOnly.FromDateTime(DateTime.UtcNow)),
            date.GetAttribute("max"));
    }

    [Fact]
    public void A_departure_cannot_be_dated_in_the_future()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0210")));

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();

        var date = page.Find("#voyage-action-date");

        Assert.False(date.HasAttribute("min"));
        Assert.Equal(
            Voyages.FormatIsoDate(DateOnly.FromDateTime(DateTime.UtcNow)),
            date.GetAttribute("max"));
    }

    [Fact]
    public void A_validation_problem_from_a_transition_is_listed_on_the_board()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0205")))
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"actualDepartureUtc":["Actual departure must not be in the future."]}}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Actual departure must not be in the future.",
            page.Find(".voyages-action-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_conflict_reports_the_state_and_reloads_the_board()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0206")))
            : Json(
                HttpStatusCode.Conflict,
                """{"title":"The voyage cannot depart from its current status.","status":409}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);
        var loadsBeforeAction = ResultQueries().Count;

        page.Find(".voyages-row-action").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "The voyage cannot depart from its current status.",
                page.Find(".voyages-action-failure").TextContent,
                StringComparison.Ordinal);
            Assert.True(ResultQueries().Count > loadsBeforeAction);
        });
    }

    [Fact]
    public void A_missing_voyage_is_reported_on_the_board()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Page(Row("VOY-0207")))
            : Json(
                HttpStatusCode.NotFound,
                """{"title":"The requested voyage was not found.","status":404}""");

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find(".voyages-row-action").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "The requested voyage was not found.",
            page.Find(".voyages-action-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_successful_action_keeps_the_selected_filters_and_page()
    {
        respond = request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return Json(
                    HttpStatusCode.OK,
                    """{"id":"20000000-0000-0000-0000-000000000001","status":"InProgress"}""");
            }

            var requestedPage = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(requestedPage, 60, Row("VOY-0208")));
        };

        var page = RenderVoyagesPage(Voyages.ManageVoyagesPermission);

        page.Find("#voyages-status").Change(Voyages.PlannedStatus);
        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("status=Planned", StringComparison.Ordinal)));

        page.FindAll(".voyages-pagination button")[1].Click();
        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("pageNumber=2", StringComparison.Ordinal)));

        page.Find(".voyages-row-action").Click();
        page.Find(".voyage-action-buttons .voyages-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = ResultQueries()[^1];

            Assert.Contains("status=Planned", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=2", lastQuery, StringComparison.Ordinal);
        });
    }

    private List<string> ResultQueries()
    {
        return requestedUris
            .Select(uri => uri.Query)
            .Where(query => query.Contains("from=", StringComparison.Ordinal)
                && !query.Contains("pageSize=1&", StringComparison.Ordinal)
                && !query.EndsWith("pageSize=1", StringComparison.Ordinal))
            .ToList();
    }

    private IRenderedComponent<Voyages> RenderVoyagesPage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.RequestUri!.AbsolutePath == "/api/fleet/vessels/lookup")
            {
                return Json(HttpStatusCode.OK, """{"vessels":[{"id":"30000000-0000-0000-0000-000000000002","name":"MV Atlantic Pioneer"}]}""");
            }

            if (request.Method != HttpMethod.Get)
            {
                writes.Add((
                    request.RequestUri!.AbsolutePath,
                    request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            }

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.AddSingleton(new FleetApiClient(httpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        if (startUri is not null)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(startUri);
        }

        var page = Render<Voyages>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string Row(
        string voyageNumber,
        string plannedDeparture = "2026-09-15T08:00:00Z",
        string plannedArrival = "2026-09-18T17:30:00Z",
        string status = "Planned",
        string? firstStopPortName = null,
        string? lastStopPortName = null,
        string stops = "null")
    {
        var firstStop = firstStopPortName is null ? "null" : $"\"{firstStopPortName}\"";
        var lastStop = lastStopPortName is null ? "null" : $"\"{lastStopPortName}\"";

        return $$"""
            {"voyageId":"20000000-0000-0000-0000-000000000001","voyageNumber":"{{voyageNumber}}","vesselId":"30000000-0000-0000-0000-000000000002","departurePortCode":"MEBAR","arrivalPortCode":"ITGOA","status":"{{status}}","plannedDeparture":"{{plannedDeparture}}","plannedArrival":"{{plannedArrival}}","actualDeparture":null,"actualArrival":null,"firstStopPortName":{{firstStop}},"lastStopPortName":{{lastStop}},"stops":{{stops}}}
            """;
    }

    private static string MappedStops()
    {
        return """
            [{"stopOrder":1,"portName":"Port of Bar","status":"Departed","latitude":42.0936,"longitude":19.0936},{"stopOrder":2,"portName":"Port of Kotor","status":"Planned","latitude":null,"longitude":null},{"stopOrder":3,"portName":"Port of Dubrovnik","status":"Planned","latitude":42.6507,"longitude":18.0944}]
            """;
    }

    private static string RowWithDeparture(string voyageNumber)
    {
        return $$"""
            {"voyageId":"20000000-0000-0000-0000-000000000001","voyageNumber":"{{voyageNumber}}","vesselId":"30000000-0000-0000-0000-000000000002","departurePortCode":"MEBAR","arrivalPortCode":"ITGOA","status":"InProgress","plannedDeparture":"2026-09-15T08:00:00Z","plannedArrival":"2026-09-18T17:30:00Z","actualDeparture":"2026-09-15T09:12:00Z","actualArrival":null}
            """;
    }

    private static string Page(params string[] rows)
    {
        return Page(1, rows.Length, rows);
    }

    private static string Page(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static string CancelledSchedule()
    {
        return """
            {"voyage":{"voyageId":"10000000-0000-0000-0000-000000000001","voyageNumber":"VOY-0215","status":"Cancelled","routeId":null,"routeName":null,"departurePortId":null,"departurePortName":null,"arrivalPortId":null,"arrivalPortName":null,"plannedDeparture":"2026-09-01T08:00:00Z","plannedArrival":"2026-09-04T17:30:00Z"},"changes":[]}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
