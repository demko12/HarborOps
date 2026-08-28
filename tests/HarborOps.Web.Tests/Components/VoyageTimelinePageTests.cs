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

public sealed class VoyageTimelinePageTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopOneId = "30000000-0000-0000-0000-000000000001";
    private const string StopTwoId = "30000000-0000-0000-0000-000000000002";
    private const string PortOneId = "40000000-0000-0000-0000-000000000001";
    private const string PortTwoId = "40000000-0000-0000-0000-000000000002";
    private const string BerthId = "50000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<(string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Itinerary());

    [Fact]
    public void The_itinerary_is_requested_for_the_voyage_in_the_route()
    {
        RenderTimelinePage();

        Assert.Equal($"/api/voyages/{VoyageId}/itinerary", requestedUris[0].AbsolutePath);
    }

    [Fact]
    public void Stops_are_rendered_in_itinerary_order_with_port_and_status()
    {
        var page = RenderTimelinePage();

        var rows = page.FindAll(".timeline-table tbody tr");

        Assert.True(rows.Count >= 2);
        Assert.Contains("Port of Bar", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Planned", page.Find(".timeline-status").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Berth_state_is_derived_from_stop_status_when_a_berth_is_assigned()
    {
        respond = _ => Json(HttpStatusCode.OK, Itinerary(firstStopStatus: "Arrived", firstStopBerthId: BerthId));

        var page = RenderTimelinePage();

        Assert.Contains("Occupied", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Delay_totals_are_summed_across_all_recorded_delays()
    {
        respond = _ => Json(HttpStatusCode.OK, Itinerary(delays:
        [
            Delay("60000000-0000-0000-0000-000000000001", null, "Weather", 30),
            Delay("60000000-0000-0000-0000-000000000002", StopOneId, "Berth unavailable", 45),
        ]));

        var page = RenderTimelinePage();

        Assert.Contains("75 min", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Voyage-level", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_itinerary_button_shows_for_managers_when_there_is_no_itinerary()
    {
        respond = _ => Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        Assert.NotEmpty(page.FindAll(".timeline-plan-itinerary-actions"));
    }

    [Fact]
    public void Plan_itinerary_button_is_hidden_for_read_only_users()
    {
        respond = _ => Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Empty(page.FindAll(".timeline-plan-itinerary-actions"));
    }

    [Fact]
    public void Plan_itinerary_button_is_hidden_once_an_itinerary_exists()
    {
        respond = _ => Json(HttpStatusCode.OK, Itinerary());

        var page = RenderTimelinePage();

        Assert.Empty(page.FindAll(".timeline-plan-itinerary-actions"));
    }

    [Fact]
    public void Opening_the_itinerary_modal_starts_with_two_empty_manual_stops()
    {
        respond = _ => Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();

        var rows = page.FindAll(".timeline-itinerary-stop");
        Assert.Equal(2, rows.Count);
        Assert.True(page.Find("#itinerary-stop-distance-0").HasAttribute("disabled"));
        Assert.Equal("0", page.Find("#itinerary-stop-distance-0").GetAttribute("value"));
    }

    [Fact]
    public void Selecting_a_route_loads_its_stops_as_read_only_rows()
    {
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/routes", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, RoutePage())
            : request.RequestUri!.AbsolutePath.Contains("/routes/", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, RouteDetailsJson())
                : Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();
        page.Find("#itinerary-route").Focus();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".voyage-picker-option")));

        page.Find(".voyage-picker-option").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".timeline-itinerary-readonly-label").Count / 3));
        Assert.Empty(page.FindAll("#itinerary-stop-port-0"));
        Assert.Contains("Port of Bar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Port of Kotor", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_and_removing_a_manual_stop_changes_the_row_count()
    {
        respond = _ => Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();
        page.Find(".timeline-itinerary-add").Click();

        Assert.Equal(3, page.FindAll(".timeline-itinerary-stop").Count);

        page.Find(".timeline-itinerary-remove").Click();

        Assert.Equal(2, page.FindAll(".timeline-itinerary-stop").Count);
    }

    [Fact]
    public void Submitting_a_manual_itinerary_posts_the_ordered_stops_with_eta_and_etd()
    {
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/ports/lookup", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, PortLookupJson())
            : request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.Created, """{"voyageId":"20000000-0000-0000-0000-000000000001","stops":[]}""")
                : Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();

        page.Find("#itinerary-stop-port-0").Focus();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".port-picker-option")));
        page.FindAll(".port-picker-option")[0].Click();
        page.Find("#itinerary-stop-eta-date-0").Change("2026-09-01");
        page.Find("#itinerary-stop-etd-date-0").Change("2026-09-01");

        page.Find("#itinerary-stop-port-1").Focus();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".port-picker-option")));
        page.FindAll(".port-picker-option")[0].Click();
        page.Find("#itinerary-stop-distance-1").Change("60");
        page.Find("#itinerary-stop-duration-1").Change("5");
        page.Find("#itinerary-stop-eta-date-1").Change("2026-09-02");
        page.Find("#itinerary-stop-etd-date-1").Change("2026-09-02");

        page.Find(".timeline-itinerary-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith("/itinerary", StringComparison.Ordinal)
                && write.Body.Contains("\"stopOrder\":1", StringComparison.Ordinal)
                && write.Body.Contains("\"stopOrder\":2", StringComparison.Ordinal)
                && write.Body.Contains("\"distanceFromPrevious\":60", StringComparison.Ordinal)
                && write.Body.Contains("\"estimatedDurationFromPrevious\":5", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_validation_failure_keeps_the_itinerary_modal_open()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"stops":["Stop orders must be unique and numbered consecutively starting at 1."]}}""")
            : Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();
        page.Find("#itinerary-stop-eta-date-0").Change("2026-09-01");
        page.Find("#itinerary-stop-etd-date-0").Change("2026-09-01");
        page.Find(".timeline-itinerary-modal .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "consecutively starting at 1",
            page.Markup,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".timeline-itinerary-modal"));
    }

    [Fact]
    public void Cancelling_the_itinerary_modal_closes_it_without_submitting()
    {
        respond = _ => Json(HttpStatusCode.OK, EmptyItinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-plan-itinerary-actions button").Click();
        Assert.NotEmpty(page.FindAll(".timeline-itinerary-modal"));

        page.Find(".voyages-modal-close").Click();

        Assert.Empty(page.FindAll(".timeline-itinerary-modal"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Assigning_a_berth_lists_options_and_posts_the_selection()
    {
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/berths", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, BerthPage())
            : request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{"stopId":"30000000-0000-0000-0000-000000000001","berthId":"50000000-0000-0000-0000-000000000001","startTime":"2026-09-01T08:00:00Z","endTime":"2026-09-01T12:00:00Z","scheduleStatus":"Reserved"}""")
                : Json(HttpStatusCode.OK, Itinerary());

        var page = RenderTimelinePage();

        page.Find(".timeline-row-action").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#timeline-berth-select")));

        page.Find("#timeline-berth-select").Focus();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".voyage-picker-option")));

        page.Find(".voyage-picker-option").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".voyage-picker-dropdown")));

        page.Find(".timeline-button").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith($"/stops/{StopOneId}/berth", StringComparison.Ordinal)));
    }

    [Fact]
    public void Recording_an_arrival_posts_the_actual_time_and_shows_confirmation()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, """{"stopId":"30000000-0000-0000-0000-000000000001","status":"Arrived"}""")
            : Json(HttpStatusCode.OK, Itinerary());

        var page = RenderTimelinePage();

        var arriveButtons = page.FindAll(".timeline-row-action");
        arriveButtons[1].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".timeline-action-form")));

        page.Find(".timeline-button").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith($"/stops/{StopOneId}/arrival", StringComparison.Ordinal)));
        page.WaitForAssertion(() => Assert.Contains("now Arrived", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_conflict_response_is_shown_inline_and_the_itinerary_is_reloaded()
    {
        var attempt = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                attempt++;
                return Json(
                    HttpStatusCode.Conflict,
                    """{"status":409,"title":"The voyage stop cannot arrive from its current status.","detail":"Voyage stop cannot arrive while Departed."}""");
            }

            return Json(HttpStatusCode.OK, Itinerary());
        };

        var page = RenderTimelinePage();

        var arriveButtons = page.FindAll(".timeline-row-action");
        arriveButtons[1].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".timeline-action-form")));

        page.Find(".timeline-button").Click();

        page.WaitForAssertion(() => Assert.Equal(1, attempt));
        page.WaitForAssertion(() => Assert.Contains(
            "Voyage stop cannot arrive while Departed",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Cancelling_an_action_closes_the_form_without_submitting()
    {
        var page = RenderTimelinePage();

        var arriveButtons = page.FindAll(".timeline-row-action");
        arriveButtons[1].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".timeline-action-form")));

        page.Find(".timeline-reset").Click();

        Assert.Empty(page.FindAll(".timeline-action-form"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Recording_a_delay_posts_the_reason_duration_and_selected_stop()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Created,
                """{"delayId":"60000000-0000-0000-0000-000000000001","voyageId":"20000000-0000-0000-0000-000000000001","voyageStopId":"30000000-0000-0000-0000-000000000001","reason":"Port congestion","description":null,"duration":90,"createdAt":"2026-08-25T10:00:00Z"}""")
            : Json(HttpStatusCode.OK, Itinerary());

        var page = RenderTimelinePage();

        page.Find("#delay-reason").Change("Port congestion");
        page.Find("#delay-duration").Change("90");
        page.Find("#delay-stop").Change(StopOneId);
        page.Find(".timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            writes,
            write => write.Path.EndsWith("/delays", StringComparison.Ordinal)
                && write.Body.Contains("\"reason\":\"Port congestion\"", StringComparison.Ordinal)
                && write.Body.Contains("\"duration\":90", StringComparison.Ordinal)
                && write.Body.Contains(StopOneId, StringComparison.Ordinal)));
        page.WaitForAssertion(() => Assert.Contains("Delay recorded", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_delay_validation_failure_is_shown_without_clearing_the_form()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"duration":["Delay duration must be greater than zero."]}}""")
            : Json(HttpStatusCode.OK, Itinerary());

        var page = RenderTimelinePage();

        page.Find("#delay-reason").Change("Port congestion");
        page.Find("#delay-duration").Change("0");
        page.Find(".timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Delay duration must be greater than zero",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Read_only_users_do_not_see_management_actions()
    {
        var page = RenderTimelinePage(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Empty(page.FindAll(".timeline-row-action"));
        Assert.Empty(page.FindAll(".timeline-delay-form"));
    }

    [Fact]
    public void A_not_found_response_shows_an_empty_state()
    {
        respond = _ => Json(HttpStatusCode.NotFound, """{"status":404,"title":"The requested voyage was not found."}""");

        var page = RenderTimelinePage();

        page.WaitForAssertion(() => Assert.Contains("Voyage not found", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_server_error_shows_a_retry_control_that_reloads_the_itinerary()
    {
        var attempt = 0;
        respond = _ =>
        {
            attempt++;
            return attempt == 1
                ? Json(HttpStatusCode.InternalServerError, """{"status":500,"title":"Unexpected error"}""")
                : Json(HttpStatusCode.OK, Itinerary());
        };

        var page = RenderTimelinePage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.True(attempt >= 2));
    }

    private IRenderedComponent<TimelinePage> RenderTimelinePage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

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
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    (permissions.Length == 0
                        ? [CurrentUserAuthorizationState.VoyagesManagePermission]
                        : permissions)
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/voyages/{VoyageId}");

        var page = Render<TimelinePage>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId)));
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Itinerary(
        string firstStopStatus = "Planned",
        string? firstStopBerthId = null,
        string[]? delays = null)
    {
        var berthField = firstStopBerthId is null
            ? "\"berthId\":null,\"berthName\":null"
            : $"\"berthId\":\"{firstStopBerthId}\",\"berthName\":\"Pier 1\"";
        var firstAta = string.Equals(firstStopStatus, "Planned", StringComparison.Ordinal)
            ? "null"
            : "\"2026-09-01T08:30:00Z\"";

        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","portCountry":"Montenegro","unLocode":"MEBAR",{{berthField}},"eta":"2026-09-01T08:00:00Z","etd":"2026-09-01T12:00:00Z","ata":{{firstAta}},"atd":null,"status":"{{firstStopStatus}}"},
                {"stopId":"{{StopTwoId}}","stopOrder":2,"portId":"{{PortTwoId}}","portName":"Port of Kotor","portCountry":"Montenegro","unLocode":"MEKOT","berthId":null,"berthName":null,"eta":"2026-09-02T08:00:00Z","etd":"2026-09-02T12:00:00Z","ata":null,"atd":null,"status":"Planned"}
            ],"delays":[{{string.Join(',', delays ?? [])}}]}
            """;
    }

    private static string Delay(string delayId, string? voyageStopId, string reason, int duration)
    {
        var stopField = voyageStopId is null ? "null" : $"\"{voyageStopId}\"";

        return $$"""
            {"delayId":"{{delayId}}","voyageStopId":{{stopField}},"reason":"{{reason}}","description":null,"duration":{{duration}},"createdAt":"2026-08-25T09:00:00Z"}
            """;
    }

    private static string BerthPage()
    {
        return $$"""
            {"items":[{"berthId":"{{BerthId}}","portId":"{{PortOneId}}","name":"Pier 1","length":200.0,"maxDraft":10.0,"status":"Available"}],"pageNumber":1,"pageSize":100,"totalCount":1}
            """;
    }

    private static string EmptyItinerary()
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[],"delays":[]}
            """;
    }

    private static string RoutePage()
    {
        return """
            {"items":[{"routeId":"60000000-0000-0000-0000-000000000001","name":"Jadranska","description":"Adriatic feeder","estimatedDistance":105.0,"estimatedDuration":9,"stopCount":2}],"pageNumber":1,"pageSize":25,"totalCount":1}
            """;
    }

    private static string RouteDetailsJson()
    {
        return $$"""
            {"routeId":"60000000-0000-0000-0000-000000000001","name":"Jadranska","description":"Adriatic feeder","estimatedDistance":60.0,"estimatedDuration":5,"stops":[
                {"portId":"{{PortOneId}}","portName":"Port of Bar","portCountry":"Montenegro","unLocode":"MEBAR","stopOrder":1,"distanceFromPrevious":0.0,"estimatedDurationFromPrevious":0},
                {"portId":"{{PortTwoId}}","portName":"Port of Kotor","portCountry":"Montenegro","unLocode":"MEKOT","stopOrder":2,"distanceFromPrevious":60.0,"estimatedDurationFromPrevious":5}
            ]}
            """;
    }

    private static string PortLookupJson()
    {
        return $$"""
            {"items":[{"portId":"{{PortOneId}}","name":"Port of Bar","country":"Montenegro","unLocode":"MEBAR"}]}
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
