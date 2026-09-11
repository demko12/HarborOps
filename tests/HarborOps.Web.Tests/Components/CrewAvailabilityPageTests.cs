using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewAvailabilityPageTests : BunitContext
{
    private const string MemberId = "10000000-0000-0000-0000-000000000001";
    private const string RequestId = "90000000-0000-0000-0000-000000000009";

    private readonly List<(HttpMethod Method, Uri Uri)> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToList =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDecision =
        _ => Json(HttpStatusCode.OK, Decision());

    [Fact]
    public void Initial_load_requests_the_availability_list_and_shows_the_empty_state()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requests, r => IsListRequest(r.Uri));
            Assert.Contains("No unavailability on record", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Rows_render_the_member_name_type_label_and_status_badge()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Annual leave", page.Markup, StringComparison.Ordinal);
            var badge = page.Find(".crew-availability-status");
            Assert.Contains("crew-availability-status-requested", badge.ClassList);
        });
    }

    [Fact]
    public void Selecting_a_type_filter_re_requests_with_the_type_query()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains(requests, r => IsListRequest(r.Uri)));

        page.Find("#crew-availability-type").Change("SickLeave");

        page.WaitForAssertion(() =>
            Assert.Contains("type=SickLeave", requests.Last(r => IsListRequest(r.Uri)).Uri.Query, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_only_users_do_not_see_the_decision_buttons()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission);
        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));

        page.Find(".crew-availability-row-heading").Click();

        Assert.Empty(page.FindAll(".crew-availability-approve"));
    }

    [Fact]
    public void Managers_can_approve_a_request_and_the_list_refreshes()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));

        page.Find(".crew-availability-row-heading").Click();
        var requestsBefore = requests.Count(r => IsListRequest(r.Uri));
        page.Find(".crew-availability-approve").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/approve", StringComparison.Ordinal));
            Assert.True(requests.Count(r => IsListRequest(r.Uri)) > requestsBefore);
        });
    }

    [Fact]
    public void A_conflict_on_approval_is_shown_in_the_conflict_banner()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));
        respondToDecision = _ => Json(HttpStatusCode.Conflict, """{"title":"Conflict","detail":"Overlaps a planned assignment."}""");

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));

        page.Find(".crew-availability-row-heading").Click();
        page.Find(".crew-availability-approve").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Overlaps a planned assignment.", page.Find(".crew-profile-form-conflict").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error_state()
    {
        respondToList = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Boom"}""");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
    }

    private static bool IsListRequest(Uri uri) => uri.AbsolutePath == "/api/crew/availability";

    private IRenderedComponent<CrewAvailability> RenderPage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!));

            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/crew/availability")
            {
                return respondToList(request);
            }

            if (path.EndsWith("/approve", StringComparison.Ordinal)
                || path.EndsWith("/reject", StringComparison.Ordinal)
                || path.EndsWith("/cancel", StringComparison.Ordinal))
            {
                return respondToDecision(request);
            }

            if (path == "/api/crew/members")
            {
                return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}""");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(httpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    (permissions.Length == 0 ? [CurrentUserAuthorizationState.CrewReadPermission] : permissions)
                        .Select(permission => new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var page = Render<CrewAvailability>();
        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private static string Page(params string[] rows) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}
        """;

    private static string Row() => $$"""
        {"id":"{{RequestId}}","crewMemberId":"{{MemberId}}","crewMemberName":"Ivan Petrović",
        "type":"AnnualLeave","status":"Requested","startDate":"2026-10-01","endDate":"2026-10-10",
        "reason":"Family holiday.","notes":null,"requestedByUserId":"20000000-0000-0000-0000-000000000002",
        "requestedAtUtc":"2026-09-01T09:00:00Z","reviewedByUserId":null,"reviewedAtUtc":null,"decisionReason":null}
        """;

    private static string Decision() => $$"""
        {"id":"{{RequestId}}","crewMemberId":"{{MemberId}}","previousStatus":"Requested","newStatus":"Approved",
        "actorUserId":"20000000-0000-0000-0000-000000000003","reason":null,"occurredAtUtc":"2026-09-10T10:00:00Z"}
        """;

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responseFactory(request);
    }
}
