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

public sealed class VoyageDelayImpactPageTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopOneId = "30000000-0000-0000-0000-000000000001";
    private const string StopTwoId = "30000000-0000-0000-0000-000000000002";
    private const string PortOneId = "40000000-0000-0000-0000-000000000001";
    private const string PortTwoId = "40000000-0000-0000-0000-000000000002";
    private const string BerthId = "50000000-0000-0000-0000-000000000001";
    private const string DelayId = "60000000-0000-0000-0000-000000000001";
    private const string RevisionBatchId = "70000000-0000-0000-0000-000000000001";
    private const string ActorId = "80000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<(string Path, string Body)> writes = [];

    private string itinerary = Itinerary(delays: [Delay(DelayId, null, "Port congestion", 120)]);
    private string estimateRevisions = EstimateRevisions();

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPreview =
        _ => Json(HttpStatusCode.OK, Preview());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToApply =
        _ => Json(HttpStatusCode.OK, AppliedRevision());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToAlert =
        _ => Json(HttpStatusCode.OK, $$"""{"revisionBatchId":"{{RevisionBatchId}}","alertCreated":true}""");

    [Fact]
    public void The_estimate_revisions_are_requested_for_the_voyage_in_the_route()
    {
        RenderTimelinePage();

        Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == $"/api/voyages/{VoyageId}/estimate-revisions");
    }

    [Fact]
    public void Assessing_a_delay_requests_the_impact_preview_without_publishing_anything()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);

        Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath
                == $"/api/voyages/{VoyageId}/delays/{DelayId}/impact-preview");
        Assert.DoesNotContain(writes, write => write.Path.EndsWith("apply-impact", StringComparison.Ordinal));
    }

    [Fact]
    public void The_preview_compares_the_current_and_the_proposed_estimates_per_stop()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);

        var card = page.Find(".timeline-impact");

        Assert.Contains("2 stops affected", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 08:00", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 10:00", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("+120 min", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("04 Sep 2026 19:30", card.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_separates_the_arrival_and_the_departure_shift()
    {
        respondToPreview = _ => Json(HttpStatusCode.OK, PreviewWithStopInProgress());

        var page = RenderTimelinePage();

        AssessImpact(page);

        var shift = page.Find(".timeline-impact tbody tr td:last-child");

        Assert.Contains("ETA0 min", shift.TextContent, StringComparison.Ordinal);
        Assert.Contains("ETD+120 min", shift.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_shows_the_berth_conflict_warnings()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);

        var warnings = page.Find(".timeline-impact-warnings");

        Assert.Contains("Pier 1", warnings.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stop 1", warnings.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_hides_the_warning_block_when_no_berth_conflicts_exist()
    {
        respondToPreview = _ => Json(HttpStatusCode.OK, Preview(withWarning: false));

        var page = RenderTimelinePage();

        AssessImpact(page);

        Assert.Empty(page.FindAll(".timeline-impact-warnings"));
    }

    [Fact]
    public void Publishing_sends_the_proposed_estimates_with_the_expected_values_and_the_reason()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);
        page.Find("#impact-reason").Change("Congestion in Bar");
        page.Find(".timeline-impact .timeline-submit").Click();

        var write = writes.Single(candidate =>
            candidate.Path == $"/api/voyages/{VoyageId}/delays/{DelayId}/apply-impact");

        Assert.Contains("\"reason\":\"Congestion in Bar\"", write.Body, StringComparison.Ordinal);
        Assert.Contains($"\"voyageStopId\":\"{StopOneId}\"", write.Body, StringComparison.Ordinal);
        Assert.Contains("\"expectedEta\":\"2026-09-01T08:00:00Z\"", write.Body, StringComparison.Ordinal);
        Assert.Contains("\"eta\":\"2026-09-01T10:00:00Z\"", write.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishing_confirms_the_revision_and_the_alert_and_refreshes_the_timeline()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);
        estimateRevisions = EstimateRevisions([Revision()]);
        page.Find(".timeline-impact .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Revised estimates published and the operations team was alerted.",
            page.Markup,
            StringComparison.Ordinal));

        Assert.Empty(page.FindAll(".timeline-impact"));
        Assert.Equal(
            2,
            requestedUris.Count(uri => uri.AbsolutePath.EndsWith(
                "/estimate-revisions",
                StringComparison.Ordinal)));
    }

    [Fact]
    public void Publishing_reports_when_the_operational_alert_could_not_be_sent()
    {
        respondToApply = _ => Json(HttpStatusCode.OK, AppliedRevision(alertCreated: false));

        var page = RenderTimelinePage();

        AssessImpact(page);
        page.Find(".timeline-impact .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Revised estimates published. The operational alert could not be sent.",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_validation_problem_keeps_the_preview_open_and_lists_the_messages()
    {
        respondToApply = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"reason":["Revision reason is required."]}}""");

        var page = RenderTimelinePage();

        AssessImpact(page);
        page.Find(".timeline-impact .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Revision reason is required.",
            page.Find(".timeline-impact .timeline-form-error").TextContent,
            StringComparison.Ordinal));

        Assert.NotEmpty(page.FindAll(".timeline-impact table"));
    }

    [Fact]
    public void A_conflict_drops_the_stale_preview_and_reloads_the_timeline()
    {
        respondToApply = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"The impact preview is no longer current.","detail":"Voyage stop changed."}""");

        var page = RenderTimelinePage();

        AssessImpact(page);
        page.Find(".timeline-impact .timeline-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "The impact preview is no longer current.",
            page.Markup,
            StringComparison.Ordinal));

        Assert.Empty(page.FindAll(".timeline-impact table"));
        Assert.Equal(
            2,
            requestedUris.Count(uri => uri.AbsolutePath.EndsWith("/itinerary", StringComparison.Ordinal)));
    }

    [Fact]
    public void Cancelling_the_preview_closes_it_without_publishing()
    {
        var page = RenderTimelinePage();

        AssessImpact(page);
        page.Find(".timeline-impact .timeline-reset").Click();

        Assert.Empty(page.FindAll(".timeline-impact"));
        Assert.DoesNotContain(writes, write => write.Path.EndsWith("apply-impact", StringComparison.Ordinal));
    }

    [Fact]
    public void The_revision_history_shows_the_actor_the_reason_and_the_old_and_new_estimates()
    {
        estimateRevisions = EstimateRevisions([Revision()]);

        var page = RenderTimelinePage();

        var revision = page.Find("[aria-labelledby='timeline-history-title'] .timeline-estimate-revision");

        Assert.Contains("Weather routing", revision.TextContent, StringComparison.Ordinal);
        Assert.Contains("02 Sep 2026 10:00", revision.TextContent, StringComparison.Ordinal);
        Assert.Contains("80000000", revision.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stop 1 — Port of Bar", revision.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "01 Sep 2026 08:00",
            revision.QuerySelector(".timeline-schedule-change-old")!.TextContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "01 Sep 2026 10:00",
            revision.QuerySelector(".timeline-schedule-change-new")!.TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_revised_stop_shows_the_original_plan_next_to_the_revised_estimate()
    {
        estimateRevisions = EstimateRevisions([Revision()], revisedFirstStop: true);

        var page = RenderTimelinePage();

        var stopRow = page.Find("[aria-labelledby='timeline-stops-title'] tbody tr");

        Assert.Contains("Revised ETA", stopRow.TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 10:00", stopRow.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(stopRow.QuerySelectorAll(".superseded"));
    }

    [Fact]
    public void An_applied_delay_can_no_longer_be_assessed()
    {
        estimateRevisions = EstimateRevisions([Revision()]);

        var page = RenderTimelinePage();

        Assert.Contains("Applied", page.Find(".timeline-impact-applied").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[aria-labelledby='timeline-delays-title'] .timeline-row-action"));
    }

    [Fact]
    public void The_alert_state_is_shown_for_every_revision()
    {
        estimateRevisions = EstimateRevisions([Revision(alertCreated: false)]);

        var page = RenderTimelinePage();

        Assert.Contains("Alert not sent", page.Find(".timeline-alert-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_the_alert_resends_it_and_refreshes_the_revision_history()
    {
        estimateRevisions = EstimateRevisions([Revision(alertCreated: false)]);

        var page = RenderTimelinePage();

        estimateRevisions = EstimateRevisions([Revision()]);
        page.Find("[aria-labelledby='timeline-history-title'] .timeline-row-action").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Alert sent",
            page.Find(".timeline-alert-state").TextContent,
            StringComparison.Ordinal));

        Assert.Contains(
            writes,
            write => write.Path
                == $"/api/voyages/{VoyageId}/estimate-revisions/{RevisionBatchId}/alert");
    }

    [Fact]
    public void A_failed_retry_is_reported_and_leaves_the_revision_in_place()
    {
        estimateRevisions = EstimateRevisions([Revision(alertCreated: false)]);
        respondToAlert = _ => Json(
            HttpStatusCode.ServiceUnavailable,
            """{"title":"Unable to resend the operational alert","detail":"The alert service is unavailable."}""");

        var page = RenderTimelinePage();

        page.Find("[aria-labelledby='timeline-history-title'] .timeline-row-action").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Unable to resend the operational alert",
            page.Markup,
            StringComparison.Ordinal));

        Assert.Contains("Alert not sent", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_only_users_cannot_assess_or_retry()
    {
        estimateRevisions = EstimateRevisions([Revision(alertCreated: false)]);

        var page = RenderTimelinePage(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Empty(page.FindAll("[aria-labelledby='timeline-history-title'] .timeline-row-action"));
        Assert.Empty(page.FindAll("[aria-labelledby='timeline-delays-title'] .timeline-row-action"));
    }

    [Fact]
    public void A_failed_revision_load_offers_a_retry()
    {
        estimateRevisions = string.Empty;

        var page = RenderTimelinePage();

        Assert.Contains("Unable to load the estimate revisions", page.Markup, StringComparison.Ordinal);

        page.Find("[aria-labelledby='timeline-history-title'] .api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Equal(
            2,
            requestedUris.Count(uri => uri.AbsolutePath.EndsWith(
                "/estimate-revisions",
                StringComparison.Ordinal))));
    }

    private void AssessImpact(IRenderedComponent<TimelinePage> page)
    {
        page.Find("[aria-labelledby='timeline-delays-title'] .timeline-row-action").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".timeline-impact")));
    }

    private IRenderedComponent<TimelinePage> RenderTimelinePage(string? permission = null)
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

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/impact-preview", StringComparison.Ordinal))
            {
                return respondToPreview(request);
            }

            if (path.EndsWith("/apply-impact", StringComparison.Ordinal))
            {
                return respondToApply(request);
            }

            if (path.EndsWith("/alert", StringComparison.Ordinal))
            {
                return respondToAlert(request);
            }

            if (path.EndsWith("/estimate-revisions", StringComparison.Ordinal))
            {
                return estimateRevisions.Length == 0
                    ? Json(HttpStatusCode.ServiceUnavailable, "{}")
                    : Json(HttpStatusCode.OK, estimateRevisions);
            }

            if (path.EndsWith("/schedule-changes", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Schedule());
            }

            return Json(HttpStatusCode.OK, itinerary);
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

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/voyages/{VoyageId}");

        var page = Render<TimelinePage>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId)));

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath.EndsWith("/estimate-revisions", StringComparison.Ordinal)));

        return page;
    }

    private static string Itinerary(string[]? delays = null)
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","portCountry":"Montenegro","unLocode":"MEBAR","berthId":"{{BerthId}}","berthName":"Pier 1","eta":"2026-09-01T08:00:00Z","etd":"2026-09-01T12:00:00Z","ata":null,"atd":null,"status":"Planned"},
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

    private static string Preview(bool withWarning = true)
    {
        var warnings = withWarning
            ? $$"""{"stopId":"{{StopOneId}}","stopOrder":1,"berthId":"{{BerthId}}","berthName":"Pier 1","conflictStart":"2026-09-01T11:00:00Z","conflictEnd":"2026-09-01T15:00:00Z"}"""
            : string.Empty;

        return $$"""
            {"voyageId":"{{VoyageId}}","delayId":"{{DelayId}}","delayMinutes":120,"plannedArrival":"2026-09-04T17:30:00Z","proposedArrival":"2026-09-04T19:30:00Z","plannedArrivalDeltaMinutes":120,"stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","status":"Planned","currentEta":"2026-09-01T08:00:00Z","currentEtd":"2026-09-01T12:00:00Z","proposedEta":"2026-09-01T10:00:00Z","proposedEtd":"2026-09-01T14:00:00Z","etaDeltaMinutes":120,"etdDeltaMinutes":120},
                {"stopId":"{{StopTwoId}}","stopOrder":2,"portId":"{{PortTwoId}}","portName":"Port of Kotor","status":"Planned","currentEta":"2026-09-02T08:00:00Z","currentEtd":"2026-09-02T12:00:00Z","proposedEta":"2026-09-02T10:00:00Z","proposedEtd":"2026-09-02T14:00:00Z","etaDeltaMinutes":120,"etdDeltaMinutes":120}
            ],"warnings":[{{warnings}}]}
            """;
    }

    private static string PreviewWithStopInProgress()
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","delayId":"{{DelayId}}","delayMinutes":120,"plannedArrival":"2026-09-04T17:30:00Z","proposedArrival":"2026-09-04T17:30:00Z","plannedArrivalDeltaMinutes":0,"stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","status":"Arrived","currentEta":"2026-09-01T08:00:00Z","currentEtd":"2026-09-01T12:00:00Z","proposedEta":"2026-09-01T08:00:00Z","proposedEtd":"2026-09-01T14:00:00Z","etaDeltaMinutes":0,"etdDeltaMinutes":120}
            ],"warnings":[]}
            """;
    }

    private static string AppliedRevision(bool alertCreated = true)
    {
        return $$"""
            {"revisionBatchId":"{{RevisionBatchId}}","delayId":"{{DelayId}}","reason":"Weather routing","revisedByUserId":"{{ActorId}}","revisedAtUtc":"2026-09-02T10:00:00Z","alertCreated":{{(alertCreated ? "true" : "false")}},"changes":[{{Change()}}]}
            """;
    }

    private static string Revision(bool alertCreated = true)
    {
        return $$"""
            {"revisionBatchId":"{{RevisionBatchId}}","delayId":"{{DelayId}}","reason":"Weather routing","revisedByUserId":"{{ActorId}}","revisedAtUtc":"2026-09-02T10:00:00Z","alertCreated":{{(alertCreated ? "true" : "false")}},"changes":[{{Change()}}]}
            """;
    }

    private static string Change()
    {
        return $$"""
            {"stopId":"{{StopOneId}}","stopOrder":1,"portName":"Port of Bar","previousEta":"2026-09-01T08:00:00Z","previousEtd":"2026-09-01T12:00:00Z","newEta":"2026-09-01T10:00:00Z","newEtd":"2026-09-01T14:00:00Z"}
            """;
    }

    private static string EstimateRevisions(string[]? revisions = null, bool revisedFirstStop = false)
    {
        var revisedFields = revisedFirstStop
            ? "\"revisedEta\":\"2026-09-01T10:00:00Z\",\"revisedEtd\":\"2026-09-01T14:00:00Z\""
            : "\"revisedEta\":null,\"revisedEtd\":null";

        return $$"""
            {"voyageId":"{{VoyageId}}","stops":[
                {"stopId":"{{StopOneId}}","stopOrder":1,"portId":"{{PortOneId}}","portName":"Port of Bar","status":"Planned","plannedEta":"2026-09-01T08:00:00Z","plannedEtd":"2026-09-01T12:00:00Z",{{revisedFields}},"ata":null,"atd":null}
            ],"revisions":[{{string.Join(',', revisions ?? [])}}]}
            """;
    }

    private static string Schedule()
    {
        return $$"""
            {"voyage":{"voyageId":"{{VoyageId}}","voyageNumber":"VOY-100","status":"Planned","routeId":null,"routeName":null,"departurePortId":"{{PortOneId}}","departurePortName":"Port of Bar","arrivalPortId":"{{PortTwoId}}","arrivalPortName":"Port of Kotor","plannedDeparture":"2026-09-01T08:00:00Z","plannedArrival":"2026-09-04T17:30:00Z"},"changes":[]}
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
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
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
