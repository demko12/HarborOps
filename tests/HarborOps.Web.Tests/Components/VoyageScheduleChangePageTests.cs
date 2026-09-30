using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TimelinePage = HarborOps.Web.Components.Pages.Voyages.VoyageTimeline;

namespace HarborOps.Web.Tests.Components;

public sealed class VoyageScheduleChangePageTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopOneId = "30000000-0000-0000-0000-000000000001";
    private const string PortOneId = "40000000-0000-0000-0000-000000000001";
    private const string PortTwoId = "40000000-0000-0000-0000-000000000002";
    private const string PortThreeId = "40000000-0000-0000-0000-000000000003";
    private const string ChangeOneId = "70000000-0000-0000-0000-000000000001";
    private const string ChangeTwoId = "70000000-0000-0000-0000-000000000002";
    private const string ActorId = "80000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<(string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToSchedule =
        _ => Json(HttpStatusCode.OK, Schedule());

    [Fact]
    public void The_schedule_changes_are_requested_for_the_voyage_in_the_route()
    {
        RenderTimelinePage();

        Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == $"/api/voyages/{VoyageId}/schedule-changes");
    }

    [Fact]
    public void The_current_schedule_is_rendered_with_ports_dates_and_status()
    {
        var page = RenderTimelinePage();

        var card = page.Find("[aria-labelledby='timeline-schedule-title']");

        Assert.Contains("VOY-100", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Port of Bar", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Port of Kotor", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 08:00", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Planned", card.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schedule_change_timeline_shows_old_and_new_values_with_actor_and_time()
    {
        respondToSchedule = _ => Json(HttpStatusCode.OK, Schedule(changes:
        [
            AmendmentChange(ChangeOneId, "2026-09-02T10:00:00Z"),
        ]));

        var page = RenderTimelinePage();

        var change = page.Find(".timeline-schedule-change");

        Assert.Contains("Amended", change.TextContent, StringComparison.Ordinal);
        Assert.Contains("Berth window moved", change.TextContent, StringComparison.Ordinal);
        Assert.Contains("02 Sep 2026 10:00", change.TextContent, StringComparison.Ordinal);
        Assert.Contains("80000000", change.TextContent, StringComparison.Ordinal);
        Assert.Contains("Port of Kotor", change.QuerySelector(".timeline-schedule-change-old")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Genoa terminal", change.TextContent, StringComparison.Ordinal);
        Assert.Contains("Planned departure", change.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_changes_are_listed_in_the_order_returned_by_the_api()
    {
        respondToSchedule = _ => Json(HttpStatusCode.OK, Schedule(
            status: "Cancelled",
            changes:
            [
                CancellationChange(ChangeTwoId, "2026-09-03T09:00:00Z"),
                AmendmentChange(ChangeOneId, "2026-09-02T10:00:00Z"),
            ]));

        var page = RenderTimelinePage();

        var changes = page.FindAll(".timeline-schedule-change");

        Assert.Equal(2, changes.Count);
        Assert.Contains("Charter withdrawn", changes[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Berth window moved", changes[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancellation_entry_reports_only_the_status_transition()
    {
        respondToSchedule = _ => Json(HttpStatusCode.OK, Schedule(
            status: "Cancelled",
            changes: [CancellationChange(ChangeTwoId, "2026-09-03T09:00:00Z")]));

        var page = RenderTimelinePage();

        var fields = page.FindAll(".timeline-schedule-change-fields li");

        var field = Assert.Single(fields);
        Assert.Contains("Status", field.TextContent, StringComparison.Ordinal);
        Assert.Contains("Planned", field.TextContent, StringComparison.Ordinal);
        Assert.Contains("Cancelled", field.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_history_shows_an_empty_state()
    {
        var page = RenderTimelinePage();

        page.Find(".timeline-history-filter[data-filter='schedule']").Click();

        Assert.Contains("No schedule changes", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".timeline-schedule-change"));
    }

    [Fact]
    public void A_history_filter_shows_only_entries_of_that_kind()
    {
        respondToSchedule = _ => Json(HttpStatusCode.OK, Schedule(changes:
        [
            AmendmentChange(ChangeOneId, "2026-09-02T10:00:00Z"),
        ]));

        var page = RenderTimelinePage();

        page.Find(".timeline-history-filter[data-filter='schedule']").Click();

        var entries = page.FindAll("[aria-labelledby='timeline-history-title'] .timeline-history-entry");
        var entry = Assert.Single(entries);
        Assert.Contains("timeline-schedule-change", entry.ClassList);
        Assert.Equal(
            "true",
            page.Find(".timeline-history-filter[data-filter='schedule']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Amend_and_cancel_actions_are_hidden_for_read_only_users()
    {
        var page = RenderTimelinePage(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Empty(page.FindAll(".timeline-schedule-amend"));
        Assert.Empty(page.FindAll(".timeline-schedule-cancel"));
    }

    [Fact]
    public void Amend_and_cancel_actions_are_hidden_once_the_voyage_left_the_planned_status()
    {
        respondToSchedule = _ => Json(HttpStatusCode.OK, Schedule(status: "InProgress"));

        var page = RenderTimelinePage();

        Assert.Empty(page.FindAll(".timeline-schedule-amend"));
        Assert.Empty(page.FindAll(".timeline-schedule-cancel"));
    }

    [Fact]
    public void The_amend_form_is_prefilled_from_the_current_schedule()
    {
        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-amend").Click();

        Assert.Equal("2026-09-01", page.Find("#amend-departure-date").GetAttribute("value"));
        Assert.Equal("08:00:00", page.Find("#amend-departure-time").GetAttribute("value"));
        Assert.Equal("2026-09-04", page.Find("#amend-arrival-date").GetAttribute("value"));
        Assert.NotEmpty(page.FindAll("#amend-reason"));
    }

    [Fact]
    public void Submitting_an_amendment_puts_the_ports_dates_and_reason()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(HttpStatusCode.OK, Schedule(changes:
            [
                AmendmentChange(ChangeOneId, "2026-09-02T10:00:00Z"),
            ]));

        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-amend").Click();
        page.Find("#amend-departure-date").Change("2026-09-02");
        page.Find("#amend-reason").Change("Berth window moved");
        page.Find(".timeline-amend-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith("/schedule", StringComparison.Ordinal)
                && write.Body.Contains($"\"departurePortId\":\"{PortOneId}\"", StringComparison.Ordinal)
                && write.Body.Contains($"\"arrivalPortId\":\"{PortTwoId}\"", StringComparison.Ordinal)
                && write.Body.Contains("\"plannedDepartureUtc\":\"2026-09-02T08:00:00Z\"", StringComparison.Ordinal)
                && write.Body.Contains("\"reason\":\"Berth window moved\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_successful_amendment_closes_the_modal_and_reloads_the_schedule()
    {
        var scheduleRequests = 0;
        respondToSchedule = request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                scheduleRequests++;

                return Json(HttpStatusCode.OK, Schedule());
            }

            return Json(HttpStatusCode.OK, Schedule());
        };

        var page = RenderTimelinePage();

        Assert.Equal(1, scheduleRequests);

        page.Find(".timeline-schedule-amend").Click();
        page.Find("#amend-reason").Change("Berth window moved");
        page.Find(".timeline-amend-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".timeline-amend-modal")));
        page.WaitForAssertion(() => Assert.Equal(2, scheduleRequests));
        page.WaitForAssertion(() => Assert.Contains("Schedule amended", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_validation_failure_keeps_the_amend_modal_open_with_the_field_errors()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"reason":["Reason is required."]}}""");

        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-amend").Click();
        page.Find(".timeline-amend-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains("Reason is required", page.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".timeline-amend-modal"));
    }

    [Fact]
    public void A_conflict_response_is_shown_inline_in_the_amend_modal()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The voyage schedule is unchanged.","detail":"Voyage already has the requested schedule."}""");

        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-amend").Click();
        page.Find("#amend-reason").Change("Berth window moved");
        page.Find(".timeline-amend-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "The voyage schedule is unchanged",
            page.Markup,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".timeline-amend-modal"));
    }

    [Fact]
    public void Closing_the_amend_modal_does_not_submit_anything()
    {
        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-amend").Click();
        Assert.NotEmpty(page.FindAll(".timeline-amend-modal"));

        page.Find(".timeline-amend-modal .voyages-modal-close").Click();

        Assert.Empty(page.FindAll(".timeline-amend-modal"));
        Assert.Empty(writes);
    }

    [Fact]
    public void The_cancel_dialog_asks_for_a_reason_before_confirming()
    {
        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-cancel").Click();

        var modal = page.Find(".timeline-cancel-modal");

        Assert.Contains("VOY-100", modal.TextContent, StringComparison.Ordinal);
        Assert.Contains("Confirm cancellation", modal.TextContent, StringComparison.Ordinal);
        Assert.Contains("Keep voyage", modal.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("#cancel-reason"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Confirming_the_cancellation_posts_the_reason_and_reloads_the_schedule()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(HttpStatusCode.OK, Schedule(
                status: "Cancelled",
                changes: [CancellationChange(ChangeTwoId, "2026-09-03T09:00:00Z")]));

        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-cancel").Click();
        page.Find("#cancel-reason").Change("Charter withdrawn");
        page.Find(".timeline-cancel-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith("/cancel", StringComparison.Ordinal)
                && write.Body.Contains("\"reason\":\"Charter withdrawn\"", StringComparison.Ordinal)));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".timeline-cancel-modal")));
        page.WaitForAssertion(() => Assert.Contains("Voyage cancelled", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Keeping_the_voyage_closes_the_cancel_dialog_without_submitting()
    {
        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-cancel").Click();
        page.Find(".timeline-cancel-modal .timeline-reset").Click();

        Assert.Empty(page.FindAll(".timeline-cancel-modal"));
        Assert.Empty(writes);
    }

    [Fact]
    public void A_cancellation_conflict_is_shown_inline_in_the_cancel_dialog()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The voyage cannot be cancelled from its current status.","detail":"Voyage cannot change its schedule while its status is InProgress."}""");

        var page = RenderTimelinePage();

        page.Find(".timeline-schedule-cancel").Click();
        page.Find("#cancel-reason").Change("Charter withdrawn");
        page.Find(".timeline-cancel-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "while its status is InProgress",
            page.Markup,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".timeline-cancel-modal"));
    }

    [Fact]
    public void A_forbidden_schedule_response_shows_a_permission_empty_state()
    {
        respondToSchedule = _ => Json(
            HttpStatusCode.Forbidden,
            """{"status":403,"title":"Forbidden"}""");

        var page = RenderTimelinePage();

        page.WaitForAssertion(() => Assert.Contains(
            "You cannot view schedule changes",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_schedule_api_failure_shows_a_retry_control_that_reloads_the_history()
    {
        var attempt = 0;
        respondToSchedule = _ =>
        {
            attempt++;

            return attempt == 1
                ? Json(HttpStatusCode.InternalServerError, """{"status":500,"title":"Unexpected error"}""")
                : Json(HttpStatusCode.OK, Schedule());
        };

        var page = RenderTimelinePage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.True(attempt >= 2));
        page.WaitForAssertion(() => Assert.Contains(
            "VOY-100",
            page.Find("[aria-labelledby='timeline-schedule-title']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_board_link_preserves_the_filters_the_operator_arrived_with()
    {
        var page = RenderTimelinePage(
            boardQuery: "from=2026-09-01&to=2026-09-30&status=Planned&page=2");

        Assert.Equal(
            "/voyages?from=2026-09-01&to=2026-09-30&status=Planned&page=2",
            page.Find(".page-breadcrumb a").GetAttribute("href"));
    }

    [Fact]
    public void The_board_link_still_preserves_the_filters_after_a_cancellation()
    {
        respondToSchedule = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Schedule())
            : Json(HttpStatusCode.OK, Schedule(
                status: "Cancelled",
                changes: [CancellationChange(ChangeTwoId, "2026-09-03T09:00:00Z")]));

        var page = RenderTimelinePage(boardQuery: "status=Planned&page=2");

        page.Find(".timeline-schedule-cancel").Click();
        page.Find("#cancel-reason").Change("Charter withdrawn");
        page.Find(".timeline-cancel-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains("Voyage cancelled", page.Markup, StringComparison.Ordinal));
        Assert.Equal(
            "/voyages?status=Planned&page=2",
            page.Find(".page-breadcrumb a").GetAttribute("href"));
    }

    [Fact]
    public async Task Disposing_the_page_cancels_the_in_flight_schedule_request()
    {
        var observed = new TaskCompletionSource<CancellationToken>();
        respondToSchedule = _ => new TaskCompletionSource<HttpResponseMessage>().Task;

        RenderTimelinePage(cancellationObserver: observed);

        var cancellationToken = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(cancellationToken.IsCancellationRequested);

        await DisposeComponentsAsync();

        Assert.True(cancellationToken.IsCancellationRequested);
    }

    private IRenderedComponent<TimelinePage> RenderTimelinePage(
        string? permission = null,
        string? boardQuery = null,
        TaskCompletionSource<CancellationToken>? cancellationObserver = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Method != HttpMethod.Get)
            {
                writes.Add((
                    request.RequestUri!.AbsolutePath,
                    request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            }

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/schedule-changes", StringComparison.Ordinal))
            {
                cancellationObserver?.TrySetResult(cancellationToken);

                return respondToSchedule(request);
            }

            if (path.EndsWith("/schedule", StringComparison.Ordinal)
                || path.EndsWith("/cancel", StringComparison.Ordinal))
            {
                return respondToSchedule(request);
            }

            if (path.EndsWith("/estimate-revisions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, EstimateRevisions());
            }

            return Json(HttpStatusCode.OK, Itinerary());
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission ?? CurrentUserAuthorizationState.VoyagesManagePermission),
                    ],
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var url = boardQuery is null
            ? $"/voyages/{VoyageId}"
            : $"/voyages/{VoyageId}?from={Uri.EscapeDataString(boardQuery)}";

        Services.GetRequiredService<NavigationManager>().NavigateTo(url);

        var page = Render<TimelinePage>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId)));
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath.EndsWith("/schedule-changes", StringComparison.Ordinal)));

        return page;
    }

    private static string EstimateRevisions()
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[],"revisions":[]}
            """;
    }

    private static string Schedule(string status = "Planned", string[]? changes = null)
    {
        return $$"""
            {"voyage":{"voyageId":"{{VoyageId}}","voyageNumber":"VOY-100","status":"{{status}}","routeId":null,"routeName":null,"departurePortId":"{{PortOneId}}","departurePortName":"Port of Bar","arrivalPortId":"{{PortTwoId}}","arrivalPortName":"Port of Kotor","plannedDeparture":"2026-09-01T08:00:00Z","plannedArrival":"2026-09-04T17:30:00Z"},"changes":[{{string.Join(',', changes ?? [])}}]}
            """;
    }

    private static string AmendmentChange(string changeId, string changedAtUtc)
    {
        return $$"""
            {"changeId":"{{changeId}}","changeType":"Amended","reason":"Berth window moved","changedByUserId":"{{ActorId}}","changedAtUtc":"{{changedAtUtc}}","previousStatus":"Planned","newStatus":"Planned","previousRouteId":null,"previousRouteName":null,"newRouteId":null,"newRouteName":null,"previousDeparturePortId":"{{PortOneId}}","previousDeparturePortName":"Port of Bar","newDeparturePortId":"{{PortOneId}}","newDeparturePortName":"Port of Bar","previousArrivalPortId":"{{PortTwoId}}","previousArrivalPortName":"Port of Kotor","newArrivalPortId":"{{PortThreeId}}","newArrivalPortName":"Genoa terminal","previousPlannedDeparture":"2026-09-01T08:00:00Z","newPlannedDeparture":"2026-09-02T08:00:00Z","previousPlannedArrival":"2026-09-04T17:30:00Z","newPlannedArrival":"2026-09-05T17:30:00Z"}
            """;
    }

    private static string CancellationChange(string changeId, string changedAtUtc)
    {
        return $$"""
            {"changeId":"{{changeId}}","changeType":"Cancelled","reason":"Charter withdrawn","changedByUserId":"{{ActorId}}","changedAtUtc":"{{changedAtUtc}}","previousStatus":"Planned","newStatus":"Cancelled","previousRouteId":null,"previousRouteName":null,"newRouteId":null,"newRouteName":null,"previousDeparturePortId":"{{PortOneId}}","previousDeparturePortName":"Port of Bar","newDeparturePortId":"{{PortOneId}}","newDeparturePortName":"Port of Bar","previousArrivalPortId":"{{PortTwoId}}","previousArrivalPortName":"Port of Kotor","newArrivalPortId":"{{PortTwoId}}","newArrivalPortName":"Port of Kotor","previousPlannedDeparture":"2026-09-01T08:00:00Z","newPlannedDeparture":"2026-09-01T08:00:00Z","previousPlannedArrival":"2026-09-04T17:30:00Z","newPlannedArrival":"2026-09-04T17:30:00Z"}
            """;
    }

    private static string Itinerary()
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","portCountry":"Montenegro","unLocode":"MEBAR","berthId":null,"berthName":null,"eta":"2026-09-01T08:00:00Z","etd":"2026-09-01T12:00:00Z","ata":null,"atd":null,"status":"Planned"}
            ],"delays":[]}
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
}
