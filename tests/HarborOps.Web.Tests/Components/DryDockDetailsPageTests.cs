using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DryDockDetailsPageTests : BunitContext
{
    private const string DryDockId = "10000000-0000-0000-0000-000000000001";
    private const string VesselId = "20000000-0000-0000-0000-000000000002";
    private const string PortId = "30000000-0000-0000-0000-000000000003";

    private readonly List<HttpRequestMessage> maintenanceRequests = [];
    private readonly List<string> maintenanceBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, Details());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPortLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"ports":[{"portId":"{{PortId}}","name":"Rotterdam","country":"Netherlands"}]}""");

    [Fact]
    public void The_details_are_requested_for_the_dry_dock_in_the_route()
    {
        RenderPage();

        Assert.Contains(
            maintenanceRequests,
            request => request.RequestUri!.AbsolutePath == $"/api/maintenance/dry-docks/{DryDockId}");
    }

    [Fact]
    public void Every_field_is_shown_with_the_looked_up_vessel_and_port_names()
    {
        var page = RenderPage();

        Assert.Equal("Aurora Borealis", page.Find(".dry-dock-details-header h1").TextContent.Trim());
        Assert.Equal("Rotterdam", page.Find(".dry-dock-details-port").TextContent.Trim());
        Assert.Contains("Hull coating", page.Find(".dry-dock-details-tasks").TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-09-01", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_name_falls_back_to_a_short_reference_when_the_lookup_returns_nothing()
    {
        respondToPortLookup = _ => Json(HttpStatusCode.OK, """{"ports":[]}""");

        var page = RenderPage();

        Assert.Equal(
            MaintenanceFormat.ShortPortReference(Guid.Parse(PortId)),
            page.Find(".dry-dock-details-port").TextContent.Trim());
    }

    [Fact]
    public void A_completed_dry_dock_shows_its_end_date_and_cost()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Details(
                endDate: "\"2026-09-20\"",
                actualCost: "91250.25",
                status: "Completed",
                allowedNextStatuses: "[]",
                daysUntilStart: "null"));

        var page = RenderPage();

        Assert.Contains("2026-09-20", page.Markup, StringComparison.Ordinal);
        Assert.Equal("91,250.25", page.Find(".dry-dock-details-cost").TextContent.Trim());
    }

    [Fact]
    public void Only_the_actions_the_api_allows_are_offered()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(".dry-dock-start"));
        Assert.NotNull(page.Find(".dry-dock-cancel"));
        Assert.Empty(page.FindAll(".dry-dock-complete"));
    }

    [Fact]
    public void A_dry_dock_in_progress_offers_only_completion()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));

        var page = RenderPage();

        Assert.NotNull(page.Find(".dry-dock-complete"));
        Assert.Empty(page.FindAll(".dry-dock-start"));
        Assert.Empty(page.FindAll(".dry-dock-cancel"));
    }

    [Fact]
    public void A_closed_dry_dock_offers_no_action_at_all()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Details(status: "Canceled", allowedNextStatuses: "[]", daysUntilStart: "null"));

        var page = RenderPage();

        Assert.Empty(page.FindAll(".dry-dock-details-actions"));
        Assert.NotNull(page.Find(".dry-dock-details-closed"));
    }

    [Fact]
    public void A_reader_without_the_manage_permission_is_offered_no_action()
    {
        var page = RenderPage(boardQuery: null, "maintenance.read");

        Assert.Empty(page.FindAll(".dry-dock-details-actions"));
        Assert.NotNull(page.Find(".dry-dock-details-readonly"));
    }

    [Fact]
    public void Starting_asks_for_confirmation_before_calling_the_api()
    {
        var page = RenderPage();
        maintenanceBodies.Clear();

        page.Find(".dry-dock-start").Click();

        Assert.NotNull(page.Find(".dry-dock-confirm-card"));
        Assert.DoesNotContain(
            maintenanceRequests,
            request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Dismissing_the_confirmation_leaves_the_dry_dock_untouched()
    {
        var page = RenderPage();

        page.Find(".dry-dock-start").Click();
        page.Find(".dry-dock-confirm-dismiss").Click();

        Assert.Empty(page.FindAll(".dry-dock-confirm-card"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_confirmed_start_posts_to_the_start_route_and_reloads()
    {
        var page = RenderPage();

        page.Find(".dry-dock-start").Click();
        page.Find(".dry-dock-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                maintenanceRequests,
                request => request.Method == HttpMethod.Post
                    && request.RequestUri!.AbsolutePath.EndsWith("/start", StringComparison.Ordinal));
            Assert.Empty(page.FindAll(".dry-dock-confirm-card"));
        });
    }

    [Fact]
    public void A_confirmed_cancel_posts_to_the_cancel_route()
    {
        var page = RenderPage();

        page.Find(".dry-dock-cancel").Click();
        page.Find(".dry-dock-confirm").Click();

        page.WaitForAssertion(() => Assert.Contains(
            maintenanceRequests,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)));
    }

    [Fact]
    public void Completing_posts_the_end_date_and_the_actual_cost()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, Transition())
            : Json(
                HttpStatusCode.OK,
                Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));

        var page = RenderPage();

        page.Find(".dry-dock-complete").Click();
        page.Find("#dry-dock-end-date").Change("2026-09-20");
        page.Find("#dry-dock-actual-cost").Change("91250.25");
        page.Find(".dry-dock-complete-form").Submit();

        page.WaitForAssertion(() =>
        {
            var body = Assert.Single(maintenanceBodies);

            Assert.Contains("2026-09-20", body, StringComparison.Ordinal);
            Assert.Contains("91250.25", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_completion_without_a_cost_is_not_sent_to_the_api()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));

        var page = RenderPage();

        page.Find(".dry-dock-complete").Click();
        page.Find("#dry-dock-end-date").Change("2026-09-20");
        maintenanceBodies.Clear();
        page.Find(".dry-dock-complete-form").Submit();

        Assert.Empty(maintenanceBodies);
        Assert.Contains(
            "Actual cost",
            page.Find(".dry-dock-details-field-errors").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_cost_is_not_sent_to_the_api()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));

        var page = RenderPage();

        page.Find(".dry-dock-complete").Click();
        page.Find("#dry-dock-end-date").Change("2026-09-20");
        page.Find("#dry-dock-actual-cost").Change("-1");
        maintenanceBodies.Clear();
        page.Find(".dry-dock-complete-form").Submit();

        Assert.Empty(maintenanceBodies);
        Assert.Contains(
            "Actual cost",
            page.Find(".dry-dock-details-field-errors").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejected_completion_reports_the_field_error_from_the_api()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"endDate":["Before the start date."]}}""")
            : Json(
                HttpStatusCode.OK,
                Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));

        var page = RenderPage();

        page.Find(".dry-dock-complete").Click();
        page.Find("#dry-dock-end-date").Change("2026-08-01");
        page.Find("#dry-dock-actual-cost").Change("100");
        page.Find(".dry-dock-complete-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "End date",
            page.Find(".dry-dock-details-field-errors").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_conflict_reports_the_reason_and_reloads_the_authoritative_status()
    {
        var postCount = 0;
        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;

                return Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Dry dock status transition is not allowed.","detail":"Already started."}""");
            }

            return Json(
                HttpStatusCode.OK,
                postCount == 0
                    ? Details()
                    : Details(status: "InProgress", allowedNextStatuses: """["Completed"]""", daysUntilStart: "null"));
        };

        var page = RenderPage();

        page.Find(".dry-dock-start").Click();
        page.Find(".dry-dock-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".dry-dock-confirm-card"));
            Assert.Contains(
                "Already started.",
                page.Find(".dry-dock-details-error").TextContent,
                StringComparison.Ordinal);
            Assert.NotNull(page.Find(".dry-dock-complete"));
        });
    }

    [Fact]
    public void An_unknown_dry_dock_reports_that_it_is_gone_without_offering_a_retry()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.NotFound,
            """{"title":"Dry dock not found.","detail":"Gone."}""");

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".empty-state"));
            Assert.Empty(page.FindAll(".api-error-state"));
        });
    }

    [Fact]
    public void A_forbidden_read_explains_the_missing_permission()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".empty-state")));
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respondToMaintenance = _ => throw new HttpRequestException("boom");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".api-error-state")));
    }

    [Fact]
    public void The_page_links_back_to_the_maintenance_workspace()
    {
        var page = RenderPage();

        Assert.Equal("/maintenance", page.Find(".dry-dock-details-back").GetAttribute("href"));
    }

    [Fact]
    public void The_back_link_returns_to_the_board_the_planner_left()
    {
        var page = RenderPage(boardQuery: "status=Planned&overdue=true");

        Assert.Equal(
            "/maintenance?status=Planned&overdue=true",
            page.Find(".dry-dock-details-back").GetAttribute("href"));
    }

    private IRenderedComponent<DryDockDetails> RenderPage(
        string? boardQuery = null,
        params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            maintenanceRequests.Add(request);

            if (request.Content is not null)
            {
                maintenanceBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return await respondToMaintenance(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var voyagesHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            respondToPortLookup(request)))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(new VoyagesApiClient(voyagesHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        // A query-string parameter can only reach the component through the address bar.
        Services.GetRequiredService<NavigationManager>().NavigateTo(boardQuery is null
            ? $"/maintenance/dry-docks/{DryDockId}"
            : $"/maintenance/dry-docks/{DryDockId}?from={Uri.EscapeDataString(boardQuery)}");

        var page = Render<DryDockDetails>(parameters => parameters
            .Add(component => component.DryDockId, Guid.Parse(DryDockId)));

        page.WaitForAssertion(() => Assert.NotEmpty(maintenanceRequests));

        return page;
    }

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

    private static string Details(
        string startDate = "2026-09-01",
        string endDate = "null",
        string plannedTasks = "Hull coating",
        string actualCost = "null",
        string status = "Planned",
        string allowedNextStatuses = """["InProgress","Canceled"]""",
        string isOverdue = "false",
        string daysUntilStart = "12")
    {
        return $$"""
            {"dryDockId":"{{DryDockId}}","vesselId":"{{VesselId}}","portId":"{{PortId}}",
            "startDate":"{{startDate}}","endDate":{{endDate}},"plannedTasks":"{{plannedTasks}}",
            "actualCost":{{actualCost}},"status":"{{status}}",
            "allowedNextStatuses":{{allowedNextStatuses}},"isOverdue":{{isOverdue}},
            "daysUntilStart":{{daysUntilStart}}}
            """;
    }

    private static string Transition()
    {
        return $$"""
            {"dryDockId":"{{DryDockId}}","status":"Completed","allowedNextStatuses":[],
            "endDate":"2026-09-20","actualCost":91250.25}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
