using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActivityPanel = HarborOps.Web.Components.Pages.Voyages.PortCallActivityPanel;

namespace HarborOps.Web.Tests.Components;

public sealed class PortCallActivityPanelTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopId = "30000000-0000-0000-0000-000000000001";
    private const string AllFastId = "40000000-0000-0000-0000-000000000001";
    private const string VoidedId = "40000000-0000-0000-0000-000000000003";
    private const string DelayId = "50000000-0000-0000-0000-000000000001";
    private const string StatementId = "60000000-0000-0000-0000-000000000001";
    private const string AllFastVersion = "70000000-0000-0000-0000-000000000001";
    private const string StatementVersion = "70000000-0000-0000-0000-000000000009";
    private const string SourceVersion = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    private readonly List<(string Method, string Path, string Body)> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = request => Route(request);

    [Fact]
    public void The_timeline_of_the_voyage_stop_is_requested_and_no_statement_is_loaded_before_one_exists()
    {
        RenderPanel();

        Assert.Equal($"/api/voyages/{VoyageId}/stops/{StopId}/activity-timeline", requests[0].Path);
        Assert.DoesNotContain(requests, request => request.Path.EndsWith("/statement", StringComparison.Ordinal));
    }

    [Fact]
    public void Entries_are_shown_chronologically_with_their_source()
    {
        var page = RenderPanel();

        var entries = page.FindAll("[data-testid=activity-entry]");
        var sources = page.FindAll("[data-testid=activity-source]").Select(source => source.TextContent.Trim());

        Assert.Equal(4, entries.Count);
        Assert.Equal(["Stop arrival", "Manual", "Delay", "Stop departure"], sources);
        Assert.Contains("All fast", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 10:20 UTC", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("45 min", entries[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 10:00 UTC", page.Find("[data-testid=activity-ata]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Stop_times_and_delays_cannot_be_corrected_from_the_activity_log()
    {
        var page = RenderPanel();

        var entries = page.FindAll("[data-testid=activity-entry]");

        Assert.DoesNotContain(entries[0].QuerySelectorAll("button"), button => button.TextContent.Trim() == "Correct");
        Assert.DoesNotContain(entries[2].QuerySelectorAll("button"), button => button.TextContent.Trim() == "Correct");
        Assert.Contains(entries[1].QuerySelectorAll("button"), button => button.TextContent.Trim() == "Correct");
    }

    [Fact]
    public void An_empty_log_shows_the_empty_state()
    {
        respond = request => Route(request, timeline: Timeline(entries: "", activities: ""));

        var page = RenderPanel();

        Assert.NotEmpty(page.FindAll(".empty-state"));
    }

    [Fact]
    public void A_reader_sees_the_log_without_any_change_controls()
    {
        var page = RenderPanel(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.NotEmpty(page.FindAll("[data-testid=activity-readonly]"));
        Assert.Empty(page.FindAll("[data-testid=activity-record]"));
        Assert.Empty(page.FindAll("[data-testid=activity-statement-start]"));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() is "Correct" or "Void");
        Assert.Contains(page.FindAll("button"), button => button.TextContent.Trim() == "History");
    }

    [Fact]
    public void An_activity_outside_the_port_call_is_flagged()
    {
        respond = request => Route(request, timeline: Timeline(entries: OutsideEntries(), activities: Activities()));

        var page = RenderPanel();

        var flagged = Assert.Single(page.FindAll("[data-testid=activity-outside]"));
        Assert.Contains("Outside ATA", flagged.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_sends_the_entered_time_with_its_explicit_offset()
    {
        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();
        page.Find("#activity-record-type").Change("CargoStarted");
        page.Find("#activity-record-occurred-date").Change("2026-09-01");
        page.Find("#activity-record-occurred-time").Change("12:30:00");
        page.Find("#activity-record-offset").Change("+02:00");
        page.Find("#activity-record-notes").Change("Hatch 2 opened");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() =>
        {
            var post = Assert.Single(requests, request => request.Method == "POST");
            Assert.EndsWith("/activities", post.Path, StringComparison.Ordinal);
            Assert.Contains("\"activityType\":\"CargoStarted\"", post.Body, StringComparison.Ordinal);
            Assert.Equal("2026-09-01T12:30:00+02:00", JsonValue(post.Body, "occurredAt"));
            Assert.Null(JsonValue(post.Body, "endedAt"));
        });
        page.WaitForAssertion(() => Assert.Contains(
            "Activity recorded.",
            page.Find("[data-testid=activity-success]").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Future_and_inverted_times_are_rejected_before_posting()
    {
        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();
        var future = DateTime.UtcNow.AddDays(2);
        page.Find("#activity-record-occurred-date").Change(future.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        page.Find(".activity-submit").Click();

        Assert.Contains("Actual events cannot be in the future.", page.Markup, StringComparison.Ordinal);

        page.Find("#activity-record-occurred-date").Change("2026-09-01");
        page.Find("#activity-record-occurred-time").Change("12:00:00");
        page.Find("#activity-record-ended-date").Change("2026-09-01");
        page.Find("#activity-record-ended-time").Change("11:00:00");
        page.Find(".activity-submit").Click();

        Assert.Contains("End time must not precede the occurrence time.", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == "POST");
    }

    [Fact]
    public void Server_validation_errors_are_shown_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"status":400,"title":"One or more validation errors occurred.","errors":{"occurredAt":["Occurrence time must not be in the future."]}}""")
            : Route(request);

        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();
        page.Find("#activity-record-occurred-date").Change("2026-09-01");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Occurrence time must not be in the future.",
            page.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_correction_requires_a_reason_and_sends_the_current_version()
    {
        var page = RenderPanel();

        Correct(page).Click();
        page.Find(".activity-submit").Click();

        Assert.Contains("Correction reason is required.", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == "POST");

        page.Find("#activity-correct-occurred-time").Change("10:25:00");
        page.Find("#activity-correct-reason").Change("Deck log shows 10:25");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() =>
        {
            var post = Assert.Single(requests, request => request.Method == "POST");
            Assert.EndsWith($"/activities/{AllFastId}/correct", post.Path, StringComparison.Ordinal);
            Assert.Contains($"\"version\":\"{AllFastVersion}\"", post.Body, StringComparison.Ordinal);
            Assert.Contains("\"reason\":\"Deck log shows 10:25\"", post.Body, StringComparison.Ordinal);
            Assert.Equal("2026-09-01T10:25:00+00:00", JsonValue(post.Body, "occurredAt"));
        });
    }

    [Fact]
    public void Voiding_requires_a_reason_and_then_posts()
    {
        var page = RenderPanel();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Void").Click();
        page.Find(".activity-submit").Click();

        Assert.Contains("Void reason is required.", page.Markup, StringComparison.Ordinal);

        page.Find("#activity-void-reason").Change("Entered twice");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith($"/activities/{AllFastId}/void", StringComparison.Ordinal)
                && request.Body.Contains("Entered twice", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_concurrent_change_is_surfaced_and_the_log_is_reloaded()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The activity log was modified concurrently.","detail":"The activity log or statement was changed by another request. Reload and try again."}""")
            : Route(request);

        var page = RenderPanel();
        var readsBefore = requests.Count(request => request.Method == "GET");

        Correct(page).Click();
        page.Find("#activity-correct-reason").Change("Fix");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "changed by another request",
            page.Markup,
            StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.True(requests.Count(request => request.Method == "GET") > readsBefore));
    }

    [Fact]
    public void The_history_of_an_activity_shows_every_version_with_its_reason()
    {
        var page = RenderPanel();

        page.FindAll("button").First(button => button.TextContent.Trim() == "History").Click();

        page.WaitForAssertion(() =>
        {
            var history = page.Find("[data-testid=activity-history]").TextContent;
            Assert.Contains("Revision 1 · Recorded", history, StringComparison.Ordinal);
            Assert.Contains("Revision 2 · Corrected", history, StringComparison.Ordinal);
            Assert.Contains("Reason: Wrong berth time", history, StringComparison.Ordinal);
            Assert.Contains("operator@harborops.test", history, StringComparison.Ordinal);
            Assert.Contains("22222222", history, StringComparison.Ordinal);
        });
        Assert.Contains(requests, request => request.Path.EndsWith($"/activities/{AllFastId}", StringComparison.Ordinal));
    }

    [Fact]
    public void Voided_activities_stay_inspectable_outside_the_timeline()
    {
        var page = RenderPanel();

        var voided = Assert.Single(page.FindAll("[data-testid=activity-voided]"));
        Assert.Contains("Pilot on board", voided.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(
            page.FindAll("[data-testid=activity-entry]"),
            entry => entry.TextContent.Contains("Pilot on board", StringComparison.Ordinal));
    }

    [Fact]
    public void A_draft_with_blockers_lists_them_and_keeps_finalization_disabled()
    {
        respond = request => Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: true));

        var page = RenderPanel();

        Assert.Equal(2, page.FindAll("[data-testid=activity-blocker]").Count);
        Assert.Contains("falls outside the port call", page.Find("[data-testid=activity-blockers]").TextContent, StringComparison.Ordinal);
        Assert.True(page.Find("[data-testid=activity-finalize]").HasAttribute("disabled"));
    }

    [Fact]
    public void Starting_a_statement_posts_and_then_shows_the_preview()
    {
        var started = false;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                started = true;

                return Json(HttpStatusCode.Created, Statement(blocked: false));
            }

            return started
                ? Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: false))
                : Route(request);
        };

        var page = RenderPanel();

        page.Find("[data-testid=activity-statement-start]").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=activity-statement-ready]")));
        Assert.Contains(requests, request => request.Method == "POST" && request.Path.EndsWith("/statement", StringComparison.Ordinal));
    }

    [Fact]
    public void Finalization_asks_for_confirmation_and_sends_both_preview_versions()
    {
        var finalized = false;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                finalized = true;

                return Json(HttpStatusCode.OK, Statement(blocked: false, status: "Finalized"));
            }

            return finalized
                ? Route(request, timeline: Timeline(statement: Summary("Finalized"), canRecord: false), statement: Statement(blocked: false, status: "Finalized"))
                : Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: false));
        };

        var page = RenderPanel();

        page.Find("[data-testid=activity-finalize]").Click();

        Assert.Contains("cannot be reopened", page.Find("[data-testid=activity-finalize-confirm]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == "POST");

        page.Find("[data-testid=activity-finalize-submit]").Click();

        page.WaitForAssertion(() =>
        {
            var post = Assert.Single(requests, request => request.Method == "POST");
            Assert.EndsWith("/statement/finalize", post.Path, StringComparison.Ordinal);
            Assert.Contains($"\"version\":\"{StatementVersion}\"", post.Body, StringComparison.Ordinal);
            Assert.Contains($"\"sourceVersion\":\"{SourceVersion}\"", post.Body, StringComparison.Ordinal);
        });
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=activity-statement-finalized]")));
    }

    [Fact]
    public void A_source_change_during_finalization_refreshes_the_preview_and_explains_the_conflict()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The statement sources changed after the preview.","detail":"A delay was recorded after the preview. Review the refreshed preview and confirm again."}""")
            : Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: false));

        var page = RenderPanel();
        var statementReads = requests.Count(request => request.Path.EndsWith("/statement", StringComparison.Ordinal));

        page.Find("[data-testid=activity-finalize]").Click();
        page.Find("[data-testid=activity-finalize-submit]").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Review the refreshed preview and confirm again.",
            page.Markup,
            StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.True(
            requests.Count(request => request.Path.EndsWith("/statement", StringComparison.Ordinal)) > statementReads));
    }

    [Fact]
    public void A_finalized_statement_locks_the_log_and_links_to_the_printable_page()
    {
        respond = request => Route(
            request,
            timeline: Timeline(statement: Summary("Finalized"), canRecord: false, locked: true),
            statement: Statement(blocked: false, status: "Finalized"));

        var page = RenderPanel();

        Assert.NotEmpty(page.FindAll("[data-testid=activity-locked]"));
        Assert.Empty(page.FindAll("[data-testid=activity-record]"));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() is "Correct" or "Void");
        Assert.Equal(
            $"/voyages/{VoyageId}/stops/{StopId}/statement/print",
            page.Find("[data-testid=activity-statement-print]").GetAttribute("href"));
        Assert.Contains("Statement finalized", page.Find("[data-testid=activity-statement-status]").TextContent, StringComparison.Ordinal);
        Assert.Contains("master@harborops.test", page.Find("[data-testid=activity-statement-finalized]").TextContent, StringComparison.Ordinal);
        Assert.Equal("SOF-VOY-2026-17-01", page.Find("[data-testid=activity-statement-number]").TextContent.Trim());
    }

    [Fact]
    public void A_stop_without_an_arrival_explains_why_nothing_can_be_recorded()
    {
        respond = request => Route(request, timeline: Timeline(entries: "", activities: "", canRecord: false, arrived: false));

        var page = RenderPanel();

        Assert.NotEmpty(page.FindAll("[data-testid=activity-not-arrived]"));
        Assert.Empty(page.FindAll("[data-testid=activity-record]"));
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_reloads_the_log()
    {
        var attempt = 0;
        respond = request =>
        {
            attempt++;

            return attempt == 1
                ? Json(HttpStatusCode.InternalServerError, """{"status":500,"title":"Unexpected error"}""")
                : Route(request);
        };

        var page = RenderPanel();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("[data-testid=activity-entry]").Count));
    }

    [Fact]
    public void A_port_with_a_known_time_zone_shows_local_times_next_to_utc()
    {
        respond = request => Route(request, timeline: Timeline(portTimeZone: "Europe/Amsterdam"));

        var page = RenderPanel();

        Assert.Equal("Europe/Amsterdam", page.Find("[data-testid=activity-port-zone]").TextContent.Trim());
        Assert.Contains("01 Sep 2026 10:20 UTC · 12:20 local", page.FindAll("[data-testid=activity-entry]")[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("12:00 local", page.Find("[data-testid=activity-ata]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Times_are_entered_in_port_local_time_by_default_and_sent_with_the_port_offset()
    {
        respond = request => Route(request, timeline: Timeline(portTimeZone: "Europe/Amsterdam"));

        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();

        var offset = page.Find("#activity-record-offset");
        Assert.Equal(ActivityPanel.PortOffset, offset.GetAttribute("value"));
        Assert.Contains("Port local time (Europe/Amsterdam)", offset.TextContent, StringComparison.Ordinal);

        page.Find("#activity-record-occurred-date").Change("2026-09-01");
        page.Find("#activity-record-occurred-time").Change("12:30:00");

        Assert.Contains(
            "Recorded as 01 Sep 2026 10:30 UTC (UTC+02:00).",
            page.Find("[data-testid=activity-record-conversion]").TextContent,
            StringComparison.Ordinal);

        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() =>
        {
            var post = Assert.Single(requests, request => request.Method == "POST");
            Assert.Equal("2026-09-01T12:30:00+02:00", JsonValue(post.Body, "occurredAt"));
        });
    }

    [Fact]
    public void Winter_dates_use_the_winter_offset_of_the_port()
    {
        respond = request => Route(request, timeline: Timeline(portTimeZone: "Europe/Amsterdam"));

        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();
        page.Find("#activity-record-occurred-date").Change("2026-01-15");
        page.Find("#activity-record-occurred-time").Change("12:30:00");

        Assert.Contains(
            "Recorded as 15 Jan 2026 11:30 UTC (UTC+01:00).",
            page.Find("[data-testid=activity-record-conversion]").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_without_a_time_zone_falls_back_to_utc_entry()
    {
        var page = RenderPanel();

        page.Find("[data-testid=activity-record]").Click();

        Assert.Equal(ActivityPanel.UtcOffset, page.Find("#activity-record-offset").GetAttribute("value"));
        Assert.DoesNotContain("Port local time", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=activity-port-zone]"));
    }

    [Fact]
    public void A_correction_is_prefilled_in_port_local_time()
    {
        respond = request => Route(request, timeline: Timeline(portTimeZone: "Europe/Amsterdam"));

        var page = RenderPanel();

        Correct(page).Click();

        Assert.Equal("12:20:00", page.Find("#activity-correct-occurred-time").GetAttribute("value"));
        Assert.Contains(
            "Recorded as 01 Sep 2026 10:20 UTC (UTC+02:00).",
            page.Find("[data-testid=activity-correct-conversion]").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocking_activity_can_be_opened_for_correction_from_the_blocker_list()
    {
        respond = request => Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: true));

        var page = RenderPanel();

        var action = Assert.Single(page.FindAll("[data-testid=activity-blocker-correct]"));
        action.Click();

        var form = page.Find("[data-testid=activity-correct-form]");
        Assert.Contains("Correction reason", form.TextContent, StringComparison.Ordinal);
        Assert.Equal("AllFast", page.Find("#activity-correct-type").GetAttribute("value"));
    }

    [Fact]
    public void Readers_get_no_correction_shortcut_on_blockers()
    {
        respond = request => Route(request, timeline: Timeline(statement: Summary("Draft")), statement: Statement(blocked: true));

        var page = RenderPanel(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Equal(2, page.FindAll("[data-testid=activity-blocker]").Count);
        Assert.Empty(page.FindAll("[data-testid=activity-blocker-correct]"));
    }

    [Fact]
    public void The_history_highlights_which_values_a_correction_changed()
    {
        var page = RenderPanel();

        page.FindAll("button").First(button => button.TextContent.Trim() == "History").Click();

        page.WaitForAssertion(() =>
        {
            var changes = Assert.Single(page.FindAll("[data-testid=activity-history-changes]"));
            var rows = changes.QuerySelectorAll("li");

            Assert.Equal(2, rows.Length);
            Assert.Contains("Occurred", rows[0].TextContent, StringComparison.Ordinal);
            Assert.Equal("01 Sep 2026 10:10 UTC", rows[0].QuerySelector("del")!.TextContent.Trim());
            Assert.Equal("01 Sep 2026 10:20 UTC", rows[0].QuerySelector("ins")!.TextContent.Trim());
            Assert.Contains("Notes", rows[1].TextContent, StringComparison.Ordinal);
            Assert.Equal("Lines ashore", rows[1].QuerySelector("ins")!.TextContent.Trim());
        });
    }

    [Fact]
    public void A_successful_change_notifies_the_timeline()
    {
        var notified = 0;
        var page = RenderPanel(onChanged: () => notified++);

        page.FindAll("button").First(button => button.TextContent.Trim() == "Void").Click();
        page.Find("#activity-void-reason").Change("Entered twice");
        page.Find(".activity-submit").Click();

        page.WaitForAssertion(() => Assert.Equal(1, notified));
    }

    private static string? JsonValue(string json, string property)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement.GetProperty(property).GetString();
    }

    private static AngleSharp.Dom.IElement Correct(IRenderedComponent<ActivityPanel> page) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == "Correct");

    private IRenderedComponent<ActivityPanel> RenderPanel(string? permission = null, Action? onChanged = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));

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
                    [
                        new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission ?? CurrentUserAuthorizationState.VoyagesManagePermission),
                    ],
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var panel = Render<ActivityPanel>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId))
            .Add(component => component.StopId, Guid.Parse(StopId))
            .Add(component => component.OnChanged, onChanged ?? (() => { })));

        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
    }

    private static Task<HttpResponseMessage> Route(
        HttpRequestMessage request,
        string? timeline = null,
        string? statement = null)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/activity-timeline", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, timeline ?? Timeline());
        }

        if (path.EndsWith("/statement", StringComparison.Ordinal))
        {
            return statement is null
                ? Json(HttpStatusCode.NotFound, """{"status":404,"title":"The requested port-call statement was not found."}""")
                : Json(HttpStatusCode.OK, statement);
        }

        if (path.Contains("/activities/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
        {
            return Json(HttpStatusCode.OK, Details());
        }

        return Json(
            HttpStatusCode.OK,
            $$"""{"activityId":"{{AllFastId}}","status":"Active","revision":3,"version":"70000000-0000-0000-0000-000000000002","actorUserId":"11111111-1111-1111-1111-111111111111","changedAtUtc":"2026-09-02T09:00:00Z"}""");
    }

    private static string Timeline(
        string? entries = null,
        string? activities = null,
        string statement = "null",
        bool canRecord = true,
        bool locked = false,
        bool arrived = true,
        string? portTimeZone = null)
    {
        return $$"""
            {
              "voyageId":"{{VoyageId}}",
              "stopId":"{{StopId}}",
              "stopStatus":"{{(arrived ? "Departed" : "Planned")}}",
              "portName":"Port of Bar",
              "portTimeZone":{{(portTimeZone is null ? "null" : $"\"{portTimeZone}\"")}},
              "berthName":"Berth 3",
              "actualArrivalUtc":{{(arrived ? "\"2026-09-01T10:00:00Z\"" : "null")}},
              "actualDepartureUtc":{{(arrived ? "\"2026-09-01T18:00:00Z\"" : "null")}},
              "canRecordActivities":{{(canRecord ? "true" : "false")}},
              "statement":{{statement}},
              "entries":[{{entries ?? Entries()}}],
              "activities":[{{activities ?? Activities(locked)}}]
            }
            """;
    }

    private static string Entries()
    {
        return $$"""
            {"position":1,"source":"StopArrival","sourceId":"{{StopId}}","sourceRevision":null,"label":"Actual arrival (ATA)","occurredAtUtc":"2026-09-01T10:00:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":false},
            {"position":2,"source":"ManualActivity","sourceId":"{{AllFastId}}","sourceRevision":2,"label":"AllFast","occurredAtUtc":"2026-09-01T10:20:00Z","endedAtUtc":null,"notes":"Lines ashore","durationMinutes":null,"isOutsidePortCall":false},
            {"position":3,"source":"Delay","sourceId":"{{DelayId}}","sourceRevision":null,"label":"Crane breakdown","occurredAtUtc":"2026-09-01T12:00:00Z","endedAtUtc":null,"notes":"Waiting for tug","durationMinutes":45,"isOutsidePortCall":false},
            {"position":4,"source":"StopDeparture","sourceId":"{{StopId}}","sourceRevision":null,"label":"Actual departure (ATD)","occurredAtUtc":"2026-09-01T18:00:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":false}
            """;
    }

    private static string OutsideEntries()
    {
        return $$"""
            {"position":1,"source":"ManualActivity","sourceId":"{{AllFastId}}","sourceRevision":1,"label":"AllFast","occurredAtUtc":"2026-09-01T09:40:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":true},
            {"position":2,"source":"StopArrival","sourceId":"{{StopId}}","sourceRevision":null,"label":"Actual arrival (ATA)","occurredAtUtc":"2026-09-01T10:00:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":false}
            """;
    }

    private static string Activities(bool locked = false)
    {
        var isLocked = locked ? "true" : "false";

        return $$"""
            {"activityId":"{{AllFastId}}","sequence":1,"activityType":"AllFast","occurredAtUtc":"2026-09-01T10:20:00Z","endedAtUtc":null,"notes":"Lines ashore","status":"Active","revision":2,"isLocked":{{isLocked}},"recordedByUserId":"11111111-1111-1111-1111-111111111111","recordedAtUtc":"2026-09-01T10:30:00Z","lastChangedByUserId":"22222222-2222-2222-2222-222222222222","lastChangedAtUtc":"2026-09-01T11:00:00Z","version":"{{AllFastVersion}}"},
            {"activityId":"{{VoidedId}}","sequence":2,"activityType":"PilotOnBoard","occurredAtUtc":"2026-09-01T09:30:00Z","endedAtUtc":null,"notes":null,"status":"Voided","revision":2,"isLocked":{{isLocked}},"recordedByUserId":"11111111-1111-1111-1111-111111111111","recordedAtUtc":"2026-09-01T10:30:00Z","lastChangedByUserId":"11111111-1111-1111-1111-111111111111","lastChangedAtUtc":"2026-09-01T11:00:00Z","version":"70000000-0000-0000-0000-000000000003"}
            """;
    }

    private static string Details()
    {
        return $$"""
            {
              "voyageId":"{{VoyageId}}",
              "stopId":"{{StopId}}",
              "activity":{{Activities().Split('\n')[0].TrimEnd().TrimEnd(',')}},
              "history":[
                {"revision":1,"changeType":"Recorded","activityType":"AllFast","occurredAtUtc":"2026-09-01T10:10:00Z","endedAtUtc":null,"notes":null,"reason":null,"actorUserId":"11111111-1111-1111-1111-111111111111","actorName":"operator@harborops.test","changedAtUtc":"2026-09-01T10:30:00Z"},
                {"revision":2,"changeType":"Corrected","activityType":"AllFast","occurredAtUtc":"2026-09-01T10:20:00Z","endedAtUtc":null,"notes":"Lines ashore","reason":"Wrong berth time","actorUserId":"22222222-2222-2222-2222-222222222222","actorName":null,"changedAtUtc":"2026-09-01T11:00:00Z"}
              ]
            }
            """;
    }

    private static string Summary(string status)
    {
        return status == "Finalized"
            ? $$"""{"statementId":"{{StatementId}}","status":"Finalized","finalizedByUserId":"33333333-3333-3333-3333-333333333333","finalizedAtUtc":"2026-09-02T08:00:00Z"}"""
            : $$"""{"statementId":"{{StatementId}}","status":"Draft","finalizedByUserId":null,"finalizedAtUtc":null}""";
    }

    private static string Statement(bool blocked, string status = "Draft")
    {
        var finalized = status == "Finalized";
        var blockers = blocked
            ? $$"""
                {"code":"StopNotDeparted","message":"The stop must be departed before its statement can be finalized; its status is 'Arrived'.","activityId":null},
                {"code":"ActivityOutsidePortCall","message":"AllFast at 2026-09-01 09:40 UTC falls outside the port call. Correct or void it.","activityId":"{{AllFastId}}"}
                """
            : string.Empty;

        return $$"""
            {
              "statementId":"{{StatementId}}",
              "documentNumber":"SOF-VOY-2026-17-01",
              "voyageId":"{{VoyageId}}",
              "stopId":"{{StopId}}",
              "status":"{{status}}",
              "version":"{{StatementVersion}}",
              "sourceVersion":"{{SourceVersion}}",
              "voyageNumber":"VOY-2026-17",
              "stopOrder":1,
              "portName":"Port of Bar",
              "portUnLocode":"MEBAR",
              "berthName":"Berth 3",
              "actualArrivalUtc":"2026-09-01T10:00:00Z",
              "actualDepartureUtc":"2026-09-01T18:00:00Z",
              "createdByUserId":"11111111-1111-1111-1111-111111111111",
              "createdAtUtc":"2026-09-01T19:00:00Z",
              "finalizedByUserId":{{(finalized ? "\"33333333-3333-3333-3333-333333333333\"" : "null")}},
              "finalizedByName":{{(finalized ? "\"master@harborops.test\"" : "null")}},
              "finalizedAtUtc":{{(finalized ? "\"2026-09-02T08:00:00Z\"" : "null")}},
              "canFinalize":{{(!blocked && !finalized ? "true" : "false")}},
              "blockers":[{{blockers}}],
              "entries":[{{Entries()}}]
            }
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
