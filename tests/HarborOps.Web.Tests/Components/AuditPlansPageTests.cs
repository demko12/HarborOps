using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Compliance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class AuditPlansPageTests : BunitContext
{
    private static readonly Guid VesselId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PlanId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private Func<HttpRequestMessage, HttpResponseMessage> responder;
    private int registerPosts;
    private int schedulePuts;
    private int assignmentPuts;
    private int startPosts;
    private int completePosts;
    private int cancelPosts;

    public AuditPlansPageTests()
    {
        responder = DefaultResponse;
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new Handler(request => responder(request))) { BaseAddress = new("https://localhost/") }));
        Services.AddSingleton(new FleetApiClient(new HttpClient(new Handler(FleetResponse)) { BaseAddress = new("https://localhost/") }));
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceManagePermission);
    }

    [Fact]
    public void Rows_are_rendered_with_type_dates_and_status()
    {
        var page = Render<AuditPlans>();

        page.WaitForAssertion(() =>
        {
            var row = page.Find(".audit-plans-row");
            Assert.Contains("Annual survey", row.TextContent);
            Assert.Contains("Scheduled", row.TextContent);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state()
    {
        responder = request => request.RequestUri!.AbsolutePath.EndsWith("/audit-plans", StringComparison.Ordinal) && request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EmptyPage)
            : DefaultResponse(request);

        var page = Render<AuditPlans>();

        page.WaitForAssertion(() => Assert.Contains("No audit plans found", page.Markup));
    }

    [Fact]
    public void An_api_failure_shows_the_error_state_with_retry()
    {
        var attempts = 0;
        responder = request => request.RequestUri!.AbsolutePath.EndsWith("/audit-plans", StringComparison.Ordinal)
            && request.Method == HttpMethod.Get && ++attempts == 1
            ? Json(HttpStatusCode.InternalServerError, "{\"title\":\"Unable to load\"}")
            : DefaultResponse(request);

        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.Contains("Unable to load", page.Markup));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("Annual survey", page.Markup));
    }

    [Fact]
    public void Without_the_manage_permission_the_schedule_button_is_not_offered()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission);

        var page = Render<AuditPlans>();

        page.WaitForAssertion(() => Assert.Contains("Annual survey", page.Markup));
        Assert.Empty(page.FindAll(".audit-plans-add-button"));
    }

    [Fact]
    public void Clicking_a_row_opens_the_detail_dialog_with_history()
    {
        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-row")));

        page.Find(".audit-plans-row").Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".audit-plans-modal"));
            Assert.Contains("Full survey.", page.Markup);
            Assert.Contains("Rescheduled", page.Markup);
        });
    }

    [Fact]
    public void Scheduling_a_new_plan_closes_the_dialog_and_refreshes_the_list()
    {
        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-add-button")));

        page.Find(".audit-plans-add-button").Click();
        page.Find("#create-type").Change("Annual survey");
        page.Find("#create-start").Change("2026-10-01");
        page.Find("#create-due").Change("2026-10-15");
        page.Find("#create-scope").Change("Full hull survey.");
        page.Find(".audit-plans-modal .audit-plans-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("#create-type"));
            Assert.Equal(1, registerPosts);
        });
    }

    [Fact]
    public void A_validation_failure_lists_field_errors_and_keeps_the_create_dialog_open()
    {
        responder = request => request.RequestUri!.AbsolutePath.EndsWith("/audit-plans", StringComparison.Ordinal) && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.BadRequest, "{\"title\":\"Validation failed\",\"errors\":{\"type\":[\"Select a supported audit type.\"]}}")
            : DefaultResponse(request);

        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-add-button")));
        page.Find(".audit-plans-add-button").Click();
        page.Find("#create-scope").Change("Scope.");
        page.Find(".audit-plans-modal .audit-plans-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Select a supported audit type.",
            page.Find(".audit-plans-form-field-errors").TextContent));
        Assert.NotEmpty(page.FindAll("#create-type"));
    }

    [Fact]
    public void A_conflict_response_shows_a_conflict_banner()
    {
        responder = request => request.RequestUri!.AbsolutePath.EndsWith("/audit-plans", StringComparison.Ordinal) && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Conflict, "{\"title\":\"Overlap\",\"detail\":\"Overlaps another active plan.\"}")
            : DefaultResponse(request);

        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-add-button")));
        page.Find(".audit-plans-add-button").Click();
        page.Find(".audit-plans-modal .audit-plans-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Overlaps another active plan.",
            page.Find(".audit-plans-form-conflict").TextContent));
    }

    [Fact]
    public void Rescheduling_submits_the_reason_and_refreshes_the_detail()
    {
        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-row")));
        page.Find(".audit-plans-row").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-modal")));

        page.Find(".audit-plans-action-buttons button").Click();
        page.Find("#reschedule-reason").Change("Yard slot moved.");
        page.Find(".audit-plans-modal .audit-plans-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Schedule updated.", page.Markup);
            Assert.Equal(1, schedulePuts);
        });
    }

    [Fact]
    public void Starting_a_plan_requires_confirmation()
    {
        responder = request => request.RequestUri!.AbsolutePath.EndsWith($"/{PlanId}", StringComparison.Ordinal) && request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, DetailJson(assignedInspector: true))
            : DefaultResponse(request);

        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-row")));
        page.Find(".audit-plans-row").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-modal")));

        page.Find(".audit-plans-start-button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-confirm")));
        page.Find(".audit-plans-confirm button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Audit started.", page.Markup);
            Assert.Equal(1, startPosts);
        });
    }

    [Fact]
    public void Cancelling_a_plan_records_the_reason()
    {
        var page = Render<AuditPlans>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-row")));
        page.Find(".audit-plans-row").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".audit-plans-modal")));

        page.Find(".audit-plans-danger-button").Click();
        page.Find("#cancel-reason").Change("Vessel sold.");
        page.Find(".audit-plans-modal .audit-plans-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Audit plan cancelled.", page.Markup);
            Assert.Equal(1, cancelPosts);
        });
    }

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

    private HttpResponseMessage DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/start", StringComparison.Ordinal)) { startPosts++; return Json(HttpStatusCode.OK, "{\"id\":\"" + PlanId + "\",\"status\":\"InProgress\",\"startedAtUtc\":\"2026-09-15T10:00:00Z\"}"); }
        if (path.EndsWith("/complete", StringComparison.Ordinal)) { completePosts++; return Json(HttpStatusCode.OK, "{\"id\":\"" + PlanId + "\",\"surveyAuditId\":1,\"completedAtUtc\":\"2026-09-15T10:00:00Z\"}"); }
        if (path.EndsWith("/cancel", StringComparison.Ordinal)) { cancelPosts++; return Json(HttpStatusCode.OK, "{\"id\":\"" + PlanId + "\",\"status\":\"Cancelled\",\"cancelledAtUtc\":\"2026-09-15T10:00:00Z\"}"); }
        if (path.EndsWith("/schedule", StringComparison.Ordinal)) { schedulePuts++; return Json(HttpStatusCode.OK, "{\"id\":\"" + PlanId + "\",\"plannedStartDate\":\"2026-10-01\",\"dueDate\":\"2026-10-15\"}"); }
        if (path.EndsWith("/assignment", StringComparison.Ordinal)) { assignmentPuts++; return Json(HttpStatusCode.OK, "{\"id\":\"" + PlanId + "\",\"inspectorUserId\":\"" + Guid.NewGuid() + "\"}"); }
        if (path.EndsWith($"/{PlanId}", StringComparison.Ordinal)) { return Json(HttpStatusCode.OK, DetailJson()); }
        if (path.EndsWith("/audit-plans", StringComparison.Ordinal) && request.Method == HttpMethod.Post) { registerPosts++; return Json(HttpStatusCode.Created, "{\"id\":\"" + PlanId + "\"}"); }
        if (path.EndsWith("/audit-plans", StringComparison.Ordinal)) { return Json(HttpStatusCode.OK, ListPage); }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static readonly string ListPage = $$"""
        {"items":[{"id":"{{PlanId}}","vesselId":"{{VesselId}}","type":"Annual survey","status":"Scheduled",
        "plannedStartDate":"2026-10-01","dueDate":"2026-10-15","assignedInspectorUserId":null,"isOverdue":false}],
        "totalCount":1,"pageNumber":1,"pageSize":25}
        """;

    private const string EmptyPage = """{"items":[],"totalCount":0,"pageNumber":1,"pageSize":25}""";

    private static string DetailJson(bool assignedInspector = false) => $$"""
        {"id":"{{PlanId}}","vesselId":"{{VesselId}}","type":"Annual survey","status":"Scheduled",
        "plannedStartDate":"2026-10-01","dueDate":"2026-10-15",
        "assignedInspectorUserId":{{(assignedInspector ? $"\"{Guid.NewGuid()}\"" : "null")}},
        "scope":"Full survey.","notes":null,"isOverdue":false,"surveyAuditId":null,
        "history":[{"id":"30000000-0000-0000-0000-000000000003","eventType":"Rescheduled","reason":"Yard slot moved.",
        "actorUserId":"{{Guid.NewGuid()}}","occurredAtUtc":"2026-09-14T09:00:00Z","previousStatus":"Scheduled","newStatus":"Scheduled",
        "previousPlannedStartDate":"2026-09-01","newPlannedStartDate":"2026-10-01","previousDueDate":"2026-09-15","newDueDate":"2026-10-15",
        "previousInspectorUserId":null,"newInspectorUserId":null}]}
        """;

    private static HttpResponseMessage FleetResponse(HttpRequestMessage request) =>
        Json(HttpStatusCode.OK, $$"""{"items":[{"vesselId":"{{VesselId}}","name":"Aurora","imoNumber":"IMO123","flagState":"ME","status":"Active"}],"pageNumber":1,"pageSize":10,"totalCount":1}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(response(request));
    }
}
