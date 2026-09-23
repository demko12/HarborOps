using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewManningPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string RankId = "50000000-0000-0000-0000-000000000001";
    private const string PlanId = "70000000-0000-0000-0000-000000000007";

    private readonly List<(HttpMethod Method, Uri Uri)> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPositions =
        _ => Json(HttpStatusCode.OK, $$"""
            {"items":[{"crewPositionId":"{{RankId}}","rankName":"Bosun","department":"Deck",
            "seniorityLevel":"Senior","crewMemberUsageCount":0,"assignmentUsageCount":0}],
            "pageNumber":1,"pageSize":100,"totalCount":1}
            """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPlans =
        _ => Json(HttpStatusCode.OK, PlansPage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToCoverage =
        _ => Json(HttpStatusCode.OK, CoveragePage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToCreate =
        _ => Json(HttpStatusCode.Created, PlanCreated());

    [Fact]
    public void Initial_state_shows_choose_a_vessel_without_loading_plans_or_coverage()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPositionsRequest(r.Uri)));

        Assert.Contains("Choose a vessel", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, r => IsPlansRequest(r.Uri));
        Assert.DoesNotContain(requests, r => IsCoverageRequest(r.Uri));
    }

    [Fact]
    public async Task Selecting_a_vessel_loads_plans_and_coverage_for_the_default_window()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPositionsRequest(r.Uri)));

        await SelectVesselAsync(page);

        page.WaitForAssertion(() =>
        {
            var plansQuery = requests.Last(r => IsPlansRequest(r.Uri)).Uri.Query;
            var coverageQuery = requests.Last(r => IsCoverageRequest(r.Uri)).Uri.Query;

            Assert.Contains($"vesselId={VesselId}", plansQuery, StringComparison.Ordinal);
            Assert.Contains($"vesselId={VesselId}", coverageQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Plan_rows_render_dates_and_line_count()
    {
        respondToPlans = _ => Json(HttpStatusCode.OK, PlansPage(PlanRow()));

        var page = RenderPage();
        await SelectVesselAsync(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("1 rank line(s)", page.Markup, StringComparison.Ordinal);
            Assert.Contains("01 Jan 2026", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Read_only_users_do_not_see_the_new_plan_button()
    {
        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission);
        await SelectVesselAsync(page);

        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPlansRequest(r.Uri)));

        Assert.Empty(page.FindAll(".crew-manning-new-plan"));
    }

    [Fact]
    public async Task Managers_can_open_the_create_form_and_submit_a_new_plan()
    {
        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        await SelectVesselAsync(page);
        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPlansRequest(r.Uri)));

        page.Find(".crew-manning-new-plan").Click();
        Assert.NotEmpty(page.FindAll(".crew-manning-create-form"));

        page.Find("#manning-create-start").Change("2026-01-01");
        page.Find("#manning-create-end").Change("2026-03-31");
        page.Find(".crew-manning-line-row select").Change(RankId);
        page.Find(".crew-manning-line-row input[type='number']").Change("2");

        var requestsBefore = requests.Count(r => IsPlansRequest(r.Uri));
        page.Find(".crew-manning-create-form button[type='submit']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/api/crew/manning-plans");
            Assert.True(requests.Count(r => IsPlansRequest(r.Uri)) > requestsBefore);
        });
    }

    [Fact]
    public async Task A_validation_failure_on_create_shows_the_field_errors()
    {
        respondToCreate = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"lines":["At least one rank line is required."]}}""");

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        await SelectVesselAsync(page);
        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPlansRequest(r.Uri)));

        page.Find(".crew-manning-new-plan").Click();
        page.Find("#manning-create-start").Change("2026-01-01");
        page.Find("#manning-create-end").Change("2026-03-31");
        page.Find(".crew-manning-create-form button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "At least one rank line is required.",
            page.Find(".crew-profile-form-field-errors").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_conflict_on_create_shows_the_conflict_banner()
    {
        respondToCreate = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Vessel already has an overlapping manning plan.","detail":"Overlaps an existing plan."}""");

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        await SelectVesselAsync(page);
        page.WaitForAssertion(() => Assert.Contains(requests, r => IsPlansRequest(r.Uri)));

        page.Find(".crew-manning-new-plan").Click();
        page.Find("#manning-create-start").Change("2026-01-01");
        page.Find("#manning-create-end").Change("2026-03-31");
        page.Find(".crew-manning-line-row select").Change(RankId);
        page.Find(".crew-manning-line-row input[type='number']").Change("2");
        page.Find(".crew-manning-create-form button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Overlaps an existing plan.",
            page.Find(".crew-profile-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shortage_intervals_are_grouped_by_rank_and_collapsed_until_expanded()
    {
        respondToCoverage = _ => Json(HttpStatusCode.OK, CoveragePage(ShortageInterval()));

        var page = RenderPage();
        await SelectVesselAsync(page);

        page.WaitForAssertion(() => Assert.Contains("Bosun", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Short 1 of 2", page.Markup, StringComparison.Ordinal);

        page.Find(".crew-manning-shortage-group-title").Click();

        Assert.Contains("Short 1 of 2", page.Markup, StringComparison.Ordinal);
        Assert.Equal("true", page.Find(".crew-manning-shortage-group-title").GetAttribute("aria-expanded"));

        page.Find(".crew-manning-shortage-group-title").Click();

        Assert.DoesNotContain("Short 1 of 2", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_shortages_shows_the_empty_state()
    {
        respondToCoverage = _ => Json(HttpStatusCode.OK, CoveragePage());

        var page = RenderPage();
        await SelectVesselAsync(page);

        page.WaitForAssertion(() => Assert.Contains("No shortages in this window", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_plans_load_shows_a_retryable_error_state()
    {
        respondToPlans = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Boom"}""");

        var page = RenderPage();
        await SelectVesselAsync(page);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        respondToPlans = _ => Json(HttpStatusCode.OK, PlansPage(PlanRow()));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("1 rank line(s)", page.Markup, StringComparison.Ordinal));
    }

    private static async Task SelectVesselAsync(IRenderedComponent<CrewManning> page)
    {
        page.Find(".vessel-picker-input").Input("Auro");

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        await page.InvokeAsync(() => page.Find(".vessel-picker-option").Click());
    }

    private static bool IsPositionsRequest(Uri uri) => uri.AbsolutePath == "/api/crew/positions";
    private static bool IsPlansRequest(Uri uri) => uri.AbsolutePath == "/api/crew/manning-plans";
    private static bool IsCoverageRequest(Uri uri) => uri.AbsolutePath == "/api/crew/manning-coverage";

    private IRenderedComponent<CrewManning> RenderPage(params string[] permissions)
    {
        var crewHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!));

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/crew/positions")
            {
                return respondToPositions(request);
            }

            if (path == "/api/crew/manning-plans")
            {
                return request.Method == HttpMethod.Post ? respondToCreate(request) : respondToPlans(request);
            }

            if (path == "/api/crew/manning-coverage")
            {
                return respondToCoverage(request);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!));

            return Json(HttpStatusCode.OK, VesselSearchPage());
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(crewHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
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

        var page = Render<CrewManning>();
        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private static string VesselSearchPage() => $$"""
        {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"5100001",
        "flagState":"Montenegro","currentStatus":"Active","vesselTypeId":"50000000-0000-0000-0000-000000000002",
        "vesselTypeName":"Bulk carrier","companyId":"60000000-0000-0000-0000-000000000001","dwt":52000,
        "updatedAtUtc":"2026-08-01T10:00:00Z"}],"pageNumber":1,"pageSize":10,"totalCount":1}
        """;

    private static string PlansPage(params string[] rows) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":1,"pageSize":50,"totalCount":{{rows.Length}}}
        """;

    private static string PlanRow() => $$"""
        {"id":"{{PlanId}}","vesselId":"{{VesselId}}","effectiveStartDate":"2026-01-01","effectiveEndDate":"2026-03-31",
        "lineCount":1,"createdByUserId":"20000000-0000-0000-0000-000000000002",
        "createdAtUtc":"2026-01-01T00:00:00Z","concurrencyToken":"30000000-0000-0000-0000-000000000003"}
        """;

    private static string PlanCreated() => $$"""
        {"id":"{{PlanId}}","vesselId":"{{VesselId}}","effectiveStartDate":"2026-01-01","effectiveEndDate":"2026-03-31",
        "concurrencyToken":"30000000-0000-0000-0000-000000000003",
        "lines":[{"id":"31000000-0000-0000-0000-000000000001","rankId":"{{RankId}}","rankName":"Bosun","requiredHeadcount":2}]}
        """;

    private static string CoveragePage(params string[] intervals) => $$"""
        {"vesselId":"{{VesselId}}","from":"2026-01-01","to":"2026-01-30","days":[],
        "shortageIntervals":[{{string.Join(',', intervals)}}]}
        """;

    private static string ShortageInterval() => $$"""
        {"rankId":"{{RankId}}","rankName":"Bosun","startDate":"2026-01-01","endDate":"2026-01-05",
        "requiredHeadcount":2,"shortage":1}
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
