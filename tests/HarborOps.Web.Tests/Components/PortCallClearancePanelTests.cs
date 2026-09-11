using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ClearancePanel = HarborOps.Web.Components.Pages.Voyages.PortCallClearancePanel;

namespace HarborOps.Web.Tests.Components;

public sealed class PortCallClearancePanelTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopId = "30000000-0000-0000-0000-000000000001";
    private const string ClearanceId = "70000000-0000-0000-0000-000000000001";
    private const string RequiredItemId = "80000000-0000-0000-0000-000000000001";
    private const string OptionalItemId = "80000000-0000-0000-0000-000000000002";
    private const string DocumentId = "90000000-0000-0000-0000-000000000001";
    private const string VesselId = "a0000000-0000-0000-0000-000000000001";

    private readonly List<(string Method, string Path, string Body)> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, PendingClearance());

    [Fact]
    public void The_clearance_is_requested_for_the_voyage_stop()
    {
        RenderPanel();

        Assert.Equal(
            $"/api/voyages/{VoyageId}/stops/{StopId}/clearance",
            requests[0].Path);
    }

    [Fact]
    public void Progress_counts_and_the_departure_block_explanation_are_shown()
    {
        var page = RenderPanel();

        Assert.Contains(
            "1 / 2 required resolved",
            page.Find("[data-testid=clearance-required-progress]").TextContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 required item(s) are neither cleared nor waived",
            page.Find("[data-testid=clearance-departure-block]").TextContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "optional item(s) still pending",
            page.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Items_are_grouped_by_category_with_evidence_and_reference_details()
    {
        var page = RenderPanel();

        var groups = page.FindAll("[data-testid=clearance-group]");

        Assert.Equal(2, groups.Count);
        Assert.Contains("Customs", groups[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Other", groups[1].TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "CUS-42",
            page.FindAll("[data-testid=clearance-item-reference]")[0].TextContent,
            StringComparison.Ordinal);
        var evidence = page.FindAll("[data-testid=clearance-item-evidence]")[0];

        Assert.Contains(
            "Customs declaration — DOC-4471",
            evidence.TextContent,
            StringComparison.Ordinal);
        Assert.Equal(DocumentId, evidence.GetAttribute("title"));
    }

    [Fact]
    public void Evidence_documents_are_picked_rather_than_typed_as_identifiers()
    {
        var page = RenderPanel();

        page.Find(".clearance-action").Click();

        Assert.Empty(page.FindAll("#clearance-add-evidence"));
        Assert.NotEmpty(page.FindAll(".document-picker"));
    }

    [Fact]
    public void A_completed_clearance_reports_that_the_stop_may_depart()
    {
        respond = _ => Json(HttpStatusCode.OK, CompletedClearance());

        var page = RenderPanel();

        Assert.Contains(
            "the stop may depart",
            page.Find("[data-testid=clearance-departure-ready]").TextContent,
            StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-testid=clearance-reopen]"));
    }

    [Fact]
    public void A_completed_clearance_offers_reopen_instead_of_adding_or_editing_items()
    {
        respond = _ => Json(HttpStatusCode.OK, CompletedClearance());

        var page = RenderPanel();

        Assert.DoesNotContain(
            page.FindAll(".clearance-action"),
            button => button.TextContent.Trim() == "Add item");
        Assert.DoesNotContain(
            page.FindAll(".clearance-item-action"),
            button => button.TextContent.Trim() == "Edit");
        Assert.Contains(
            page.FindAll(".clearance-item-action"),
            button => button.TextContent.Trim() == "Clear");
        Assert.NotEmpty(page.FindAll("[data-testid=clearance-reopen]"));
    }

    [Fact]
    public void Read_only_users_see_the_checklist_without_any_action_controls()
    {
        var page = RenderPanel(CurrentUserAuthorizationState.VoyagesReadPermission);

        Assert.Empty(page.FindAll(".clearance-item-action"));
        Assert.Empty(page.FindAll(".clearance-action"));
    }

    [Fact]
    public void A_departed_stop_locks_the_checklist_for_managers()
    {
        var page = RenderPanel(stopStatus: "Departed");

        Assert.Contains("This stop has departed", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".clearance-item-action"));
        Assert.Empty(page.FindAll(".clearance-action"));
    }

    [Fact]
    public void A_recorded_departure_locks_the_checklist_even_when_the_status_is_not_Departed()
    {
        var page = RenderPanel(
            stopStatus: "Completed",
            stopDepartedAtUtc: new DateTime(2026, 8, 10, 8, 20, 0, DateTimeKind.Utc));

        Assert.Contains("This stop has departed", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".clearance-item-action"));
        Assert.Empty(page.FindAll(".clearance-action"));
    }

    [Fact]
    public void A_departed_stop_never_claims_that_departure_is_blocked()
    {
        var page = RenderPanel(
            stopStatus: "Completed",
            stopDepartedAtUtc: new DateTime(2026, 8, 10, 8, 20, 0, DateTimeKind.Utc));

        Assert.Empty(page.FindAll("[data-testid=clearance-departure-block]"));
        Assert.Empty(page.FindAll("[data-testid=clearance-departure-ready]"));
        Assert.Contains(
            "The stop departed on 10 Aug 2026 08:20.",
            page.Find("[data-testid=clearance-departure-done]").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Overdue_required_items_are_flagged_on_the_item_and_summarised_in_the_progress()
    {
        respond = _ => Json(HttpStatusCode.OK, OverdueClearance());

        var page = RenderPanel();

        Assert.Contains(
            "1 required item(s) are past their due time.",
            page.Find("[data-testid=clearance-overdue-note]").TextContent,
            StringComparison.Ordinal);
        Assert.Single(page.FindAll("[data-testid=clearance-item-overdue]"));
        Assert.NotEmpty(page.FindAll("dd.clearance-due-overdue"));
    }

    [Fact]
    public void Nothing_is_flagged_as_overdue_when_every_required_item_is_still_in_time()
    {
        var page = RenderPanel();

        Assert.Empty(page.FindAll("[data-testid=clearance-overdue-note]"));
        Assert.Empty(page.FindAll("[data-testid=clearance-item-overdue]"));
        Assert.Empty(page.FindAll("dd.clearance-due-overdue"));
    }

    [Fact]
    public void Adding_an_item_posts_the_captured_values()
    {
        var page = RenderPanel();

        page.Find(".clearance-action").Click();
        page.Find("#clearance-add-title").Change("Customs declaration");
        page.Find("#clearance-add-reference").Change("CUS-9");
        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith("/clearance/items", StringComparison.Ordinal)));

        var body = requests.Last(request => request.Method == "POST").Body;
        Assert.Contains("\"title\":\"Customs declaration\"", body, StringComparison.Ordinal);
        Assert.Contains("\"authorityReference\":\"CUS-9\"", body, StringComparison.Ordinal);
        Assert.Contains("\"isRequired\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_item_without_a_title_is_blocked_before_any_request_is_sent()
    {
        var page = RenderPanel();
        var requestsBefore = requests.Count;

        page.Find(".clearance-action").Click();
        page.Find(".clearance-submit").Click();

        Assert.Contains("Title is required.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(requestsBefore, requests.Count);
    }

    [Fact]
    public void Cancelling_the_add_form_discards_it_without_sending_a_request()
    {
        var page = RenderPanel();
        var requestsBefore = requests.Count;

        page.Find(".clearance-action").Click();
        page.Find("#clearance-add-title").Change("Customs declaration");
        page.Find(".clearance-cancel").Click();

        Assert.Empty(page.FindAll("#clearance-add-title"));
        Assert.Equal(requestsBefore, requests.Count);
    }

    [Fact]
    public void Editing_a_pending_item_puts_the_new_values()
    {
        var page = RenderPanel();

        page.FindAll(".clearance-item-action")[0].Click();
        page.Find("#clearance-edit-title").Change("Amended customs declaration");
        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "PUT"
                && request.Path.EndsWith($"/clearance/items/{RequiredItemId}", StringComparison.Ordinal)));
    }

    [Fact]
    public void Clearing_an_item_shows_a_confirmation_before_it_posts()
    {
        var page = RenderPanel();

        page.FindAll(".clearance-item-action")[1].Click();

        Assert.Contains("Clearing is permanent", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            requests,
            request => request.Path.EndsWith("/clear", StringComparison.Ordinal));

        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith($"/clearance/items/{RequiredItemId}/clear", StringComparison.Ordinal)));
    }

    [Fact]
    public void Waiving_an_item_requires_a_reason_before_it_posts()
    {
        var page = RenderPanel();
        var requestsBefore = requests.Count;

        page.FindAll(".clearance-item-action")[2].Click();
        page.Find(".clearance-submit").Click();

        Assert.Contains("Waiver reason is required.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(requestsBefore, requests.Count);

        page.Find("#clearance-waive-reason").Change("Health authority waived it.");
        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith($"/clearance/items/{RequiredItemId}/waive", StringComparison.Ordinal)));
    }

    [Fact]
    public void Completion_is_disabled_while_a_required_item_is_pending()
    {
        var page = RenderPanel();

        Assert.True(page.Find("[data-testid=clearance-complete]").HasAttribute("disabled"));
    }

    [Fact]
    public void Completion_asks_for_confirmation_and_then_posts()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, ReadyClearance())
            : Json(HttpStatusCode.OK, StatusChange("Completed"));

        var page = RenderPanel();

        page.Find("[data-testid=clearance-complete]").Click();

        Assert.Contains("Complete the clearance for this port call?", page.Markup, StringComparison.Ordinal);

        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith("/clearance/complete", StringComparison.Ordinal)));
    }

    [Fact]
    public void Reopening_requires_a_reason_and_then_posts()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, CompletedClearance())
            : Json(HttpStatusCode.OK, StatusChange("Reopened"));

        var page = RenderPanel();
        var requestsBefore = requests.Count;

        page.Find("[data-testid=clearance-reopen]").Click();
        page.Find(".clearance-submit").Click();

        Assert.Contains("Reopen reason is required.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(requestsBefore, requests.Count);

        page.Find("#clearance-reopen-reason").Change("Customs raised a new query.");
        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == "POST"
                && request.Path.EndsWith("/clearance/reopen", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_conflict_is_surfaced_and_the_clearance_is_refreshed()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, PendingClearance())
            : Json(
                HttpStatusCode.Conflict,
                """{"status":409,"title":"The clearance item is no longer pending.","detail":"Someone else already cleared it."}""");

        var page = RenderPanel();
        var readsBefore = requests.Count(request => request.Method == "GET");

        page.FindAll(".clearance-item-action")[1].Click();
        page.Find(".clearance-submit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Someone else already cleared it.",
            page.Markup,
            StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.True(
            requests.Count(request => request.Method == "GET") > readsBefore));
    }

    [Fact]
    public void The_history_lists_every_recorded_event_with_its_actor()
    {
        var page = RenderPanel();

        var history = page.Find(".clearance-history").TextContent;

        Assert.Contains("Item added", history, StringComparison.Ordinal);
        Assert.Contains("Item cleared", history, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_shows_a_retry_control_that_reloads_the_clearance()
    {
        var attempt = 0;
        respond = _ =>
        {
            attempt++;

            return attempt == 1
                ? Json(HttpStatusCode.InternalServerError, """{"status":500,"title":"Unexpected error"}""")
                : Json(HttpStatusCode.OK, PendingClearance());
        };

        var page = RenderPanel();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.True(attempt >= 2));
    }

    [Fact]
    public void Actor_identifiers_are_shortened_with_the_full_value_kept_in_the_title()
    {
        var page = RenderPanel();

        var actor = page.FindAll(".clearance-actor")[0];

        Assert.Equal("11111111", actor.TextContent.Trim());
        Assert.Equal("11111111-1111-1111-1111-111111111111", actor.GetAttribute("title"));
    }

    [Fact]
    public void Due_times_are_shown_as_a_relative_state_rather_than_a_raw_timestamp()
    {
        var page = RenderPanel();

        var due = page.FindAll("[data-testid=clearance-item-due]")[0];

        Assert.Equal("Due today", due.TextContent.Trim());
        Assert.Contains("2026", due.GetAttribute("title")!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overdue_required_item_reports_its_due_state_as_overdue()
    {
        respond = _ => Json(HttpStatusCode.OK, OverdueClearance());

        var page = RenderPanel();

        Assert.Equal(
            "Overdue",
            page.FindAll("[data-testid=clearance-item-due]")[0].TextContent.Trim());
    }

    [Fact]
    public void Each_category_group_reports_how_many_of_its_items_are_resolved()
    {
        var page = RenderPanel();

        var summaries = page.FindAll("[data-testid=clearance-group-summary]");

        Assert.Equal(2, summaries.Count);
        Assert.Equal("0/1 resolved", summaries[0].TextContent.Trim());
    }

    [Fact]
    public void Required_pending_items_are_marked_as_blocking_departure()
    {
        var page = RenderPanel();

        var blocking = Assert.Single(page.FindAll("[data-testid=clearance-item-blocking]"));

        Assert.Contains("Customs declaration", blocking.TextContent, StringComparison.Ordinal);
    }

    private IRenderedComponent<ClearancePanel> RenderPanel(
        string? permission = null,
        string stopStatus = "Arrived",
        DateTime? stopDepartedAtUtc = null)
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
        Services.AddSingleton(new DocumentsApiClient(
            new HttpClient(new StubHttpMessageHandler(_ => Json(HttpStatusCode.OK, Document())))
            {
                BaseAddress = new Uri("https://api.harborops.test/"),
            }));
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

        var panel = Render<ClearancePanel>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId))
            .Add(component => component.VesselId, Guid.Parse(VesselId))
            .Add(component => component.StopId, Guid.Parse(StopId))
            .Add(component => component.StopStatus, stopStatus)
            .Add(component => component.StopDepartedAtUtc, stopDepartedAtUtc));

        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
    }

    private static string PendingClearance()
    {
        return Clearance(
            "Pending",
            requiredResolved: 1,
            requiredPending: 1,
            canComplete: false,
            requiredItemStatus: "Pending");
    }

    private static string ReadyClearance()
    {
        return Clearance(
            "Pending",
            requiredResolved: 2,
            requiredPending: 0,
            canComplete: true,
            requiredItemStatus: "Cleared");
    }

    private static string OverdueClearance()
    {
        return Clearance(
            "Pending",
            requiredResolved: 1,
            requiredPending: 1,
            canComplete: false,
            requiredItemStatus: "Pending",
            overdueRequired: 1);
    }

    private static string CompletedClearance()
    {
        return Clearance(
            "Completed",
            requiredResolved: 2,
            requiredPending: 0,
            canComplete: true,
            requiredItemStatus: "Cleared",
            completedAtUtc: "\"2026-09-01T13:00:00Z\"");
    }

    private static string Document()
    {
        return $$"""
            {
              "id":"{{DocumentId}}",
              "vesselId":"{{VesselId}}",
              "documentTypeId":1,
              "documentTypeName":"Customs declaration",
              "number":"DOC-4471",
              "issueDate":"2026-08-01",
              "expiryDate":"2027-08-01",
              "issuingAuthority":"Port of Bar",
              "status":"Valid",
              "reviewStatus":"Approved",
              "hasFile":false,
              "fileName":null,
              "contentType":null,
              "sizeBytes":null,
              "uploadedBy":null,
              "uploadedAtUtc":null
            }
            """;
    }

    private static string Clearance(
        string status,
        int requiredResolved,
        int requiredPending,
        bool canComplete,
        string requiredItemStatus,
        string completedAtUtc = "null",
        int overdueRequired = 0)
    {
        return $$"""
            {
              "voyageId":"{{VoyageId}}",
              "stopId":"{{StopId}}",
              "clearanceId":"{{ClearanceId}}",
              "status":"{{status}}",
              "completedByUserId":null,
              "completedAtUtc":{{completedAtUtc}},
              "evaluatedAtUtc":"2026-09-01T12:00:00Z",
              "progress":{
                "totalItemCount":3,
                "requiredItemCount":2,
                "requiredResolvedCount":{{requiredResolved}},
                "requiredPendingCount":{{requiredPending}},
                "overdueRequiredItemCount":{{overdueRequired}},
                "optionalItemCount":1,
                "optionalPendingCount":1,
                "clearedCount":{{requiredResolved}},
                "waivedCount":0,
                "canComplete":{{(canComplete ? "true" : "false")}}
              },
              "items":[
                {"itemId":"{{RequiredItemId}}","category":"Customs","title":"Customs declaration","isRequired":true,"dueAtUtc":"2026-09-01T16:00:00Z","authorityReference":"CUS-42","evidenceDocumentId":"{{DocumentId}}","status":"{{requiredItemStatus}}","isOverdue":{{(overdueRequired > 0 ? "true" : "false")}},"decisionReason":null,"decidedByUserId":null,"decidedAtUtc":null,"addedByUserId":"11111111-1111-1111-1111-111111111111","addedAtUtc":"2026-09-01T12:00:00Z"},
                {"itemId":"{{OptionalItemId}}","category":"Other","title":"Optional courtesy note","isRequired":false,"dueAtUtc":null,"authorityReference":null,"evidenceDocumentId":null,"status":"Pending","decisionReason":null,"decidedByUserId":null,"decidedAtUtc":null,"addedByUserId":"11111111-1111-1111-1111-111111111111","addedAtUtc":"2026-09-01T12:00:00Z"}
              ],
              "history":[
                {"eventId":"a0000000-0000-0000-0000-000000000001","itemId":"{{RequiredItemId}}","sequence":1,"eventType":"ItemAdded","reason":null,"actorUserId":"11111111-1111-1111-1111-111111111111","occurredAtUtc":"2026-09-01T12:00:00Z"},
                {"eventId":"a0000000-0000-0000-0000-000000000002","itemId":"{{RequiredItemId}}","sequence":2,"eventType":"ItemCleared","reason":null,"actorUserId":"11111111-1111-1111-1111-111111111111","occurredAtUtc":"2026-09-01T12:30:00Z"}
              ]
            }
            """;
    }

    private static string StatusChange(string status)
    {
        return $$"""
            {"clearanceId":"{{ClearanceId}}","stopId":"{{StopId}}","status":"{{status}}","actorUserId":"11111111-1111-1111-1111-111111111111","occurredAtUtc":"2026-09-01T13:00:00Z"}
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
