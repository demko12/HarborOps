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

public sealed class DryDocksPanelTests : BunitContext
{
    private const string DryDockId = "10000000-0000-0000-0000-000000000001";
    private const string VesselId = "20000000-0000-0000-0000-000000000002";
    private const string PortId = "30000000-0000-0000-0000-000000000003";

    private readonly List<Uri> maintenanceUris = [];
    private readonly List<string> maintenanceBodies = [];
    private readonly List<Uri> voyagesUris = [];

    private string? boardUrl;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, DryDockPage(DryDock()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPortLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"ports":[{"id":"{{PortId}}","name":"Rotterdam","country":"Netherlands"}]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPortSearch =
        _ => Json(
            HttpStatusCode.OK,
            $$"""
            {"items":[{"portId":"{{PortId}}","name":"Rotterdam","city":"Rotterdam",
            "country":"Netherlands","unLocode":"NLRTM","shipyardName":"Damen"}],
            "pageNumber":1,"pageSize":10,"totalCount":1}
            """);

    [Fact]
    public void The_board_lists_dry_docks_with_looked_up_vessel_and_port_names()
    {
        var panel = RenderPanel();

        var row = panel.Find(".dry-dock-row");

        Assert.Equal("Aurora Borealis", panel.Find(".dry-dock-vessel").TextContent.Trim());
        Assert.Equal("Rotterdam", panel.Find(".dry-dock-port").TextContent.Trim());
        Assert.Contains("2026-09-01", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Hull coating", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_name_falls_back_to_a_short_reference_when_the_lookup_returns_nothing()
    {
        respondToPortLookup = _ => Json(HttpStatusCode.OK, """{"ports":[]}""");

        var panel = RenderPanel();

        Assert.Equal(
            MaintenanceFormat.ShortPortReference(Guid.Parse(PortId)),
            panel.Find(".dry-dock-port").TextContent.Trim());
    }

    [Fact]
    public void An_unreachable_port_directory_still_renders_the_board()
    {
        respondToPortLookup = _ => throw new HttpRequestException("boom");

        var panel = RenderPanel();

        Assert.NotNull(panel.Find(".dry-dock-row"));
        Assert.Contains("Hull coating", panel.Find(".dry-dock-row").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overdue_dry_dock_is_marked_with_text_not_colour_alone()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            DryDockPage(DryDock(isOverdue: "true", daysUntilStart: "-4")));

        var panel = RenderPanel();

        Assert.Equal("Overdue", panel.Find(".dry-dock-timing").TextContent.Trim());
    }

    [Fact]
    public void A_completed_dry_dock_shows_its_end_date_and_cost()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            DryDockPage(DryDock(
                endDate: "\"2026-09-20\"",
                actualCost: "91250.25",
                status: "Completed",
                daysUntilStart: "null")));

        var panel = RenderPanel();
        var cells = panel.Find(".dry-dock-row").QuerySelectorAll("td");

        Assert.Equal("2026-09-20", cells[3].TextContent.Trim());
        Assert.Equal("91,250.25", cells[5].TextContent.Trim());
        Assert.Contains("Completed", cells[6].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_in_progress_status_is_shown_in_readable_words()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            DryDockPage(DryDock(status: "InProgress", daysUntilStart: "null")));

        var panel = RenderPanel();

        Assert.Contains(
            "In progress",
            panel.Find(".maintenance-status").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_links_to_the_details_page()
    {
        var panel = RenderPanel();

        // Even with no filters the tab travels, so the back link returns to this board
        // instead of the work order list the workspace opens with.
        Assert.Equal(
            $"/maintenance/dry-docks/{DryDockId}?from=tab%3Ddry-docks",
            panel.Find(".dry-dock-link").GetAttribute("href"));
    }

    [Fact]
    public void A_details_link_carries_the_active_filters_so_it_can_return_to_them()
    {
        var panel = RenderPanel();

        panel.Find("#dry-dock-status").Change("Planned");
        panel.WaitForAssertion(() => Assert.Contains(
            maintenanceUris,
            uri => uri.Query.Contains("status=Planned", StringComparison.Ordinal)));

        var href = panel.Find(".dry-dock-link").GetAttribute("href");

        Assert.Contains("from=", href, StringComparison.Ordinal);
        Assert.Contains("status%3DPlanned", href, StringComparison.Ordinal);
    }

    [Fact]
    public void The_board_reopens_with_the_filters_the_details_page_returned_with()
    {
        boardUrl = $"/maintenance?tab=dry-docks&vessel={VesselId}&status=Planned&overdue=true&search=Hull";

        RenderPanel();

        var query = maintenanceUris[0].Query;

        Assert.Contains($"vesselId={VesselId}", query, StringComparison.Ordinal);
        Assert.Contains("status=Planned", query, StringComparison.Ordinal);
        Assert.Contains("overdue=true", query, StringComparison.Ordinal);
        Assert.Contains("search=Hull", query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_returned_page_number_is_reopened_with_the_board()
    {
        boardUrl = "/maintenance?tab=dry-docks&page=3";

        RenderPanel();

        Assert.Contains("pageNumber=3", maintenanceUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_the_api_would_reject_is_dropped_instead_of_being_sent()
    {
        boardUrl = "/maintenance?tab=dry-docks&status=Sunk";

        RenderPanel();

        Assert.DoesNotContain("status=", maintenanceUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_filter_is_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#dry-dock-status").Change("Completed");

        panel.WaitForAssertion(() => Assert.Contains(
            maintenanceUris,
            uri => uri.Query.Contains("status=Completed", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_overdue_toggle_is_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find(".dry-dock-overdue-toggle input").Change(true);

        panel.WaitForAssertion(() => Assert.Contains(
            maintenanceUris,
            uri => uri.Query.Contains("overdue=true", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resetting_clears_every_filter_from_the_request()
    {
        var panel = RenderPanel();

        panel.Find("#dry-dock-status").Change("Planned");
        panel.WaitForAssertion(() => Assert.Contains(
            maintenanceUris,
            uri => uri.Query.Contains("status=Planned", StringComparison.Ordinal)));

        maintenanceUris.Clear();
        panel.Find(".dry-dock-clear").Click();

        panel.WaitForAssertion(() =>
        {
            var latest = Assert.Single(maintenanceUris);

            Assert.DoesNotContain("status=", latest.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("overdue=", latest.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_schedule_form_stays_closed_until_it_is_asked_for()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".dry-dock-form-card"));

        panel.Find(".dry-dock-schedule-toggle").Click();

        Assert.NotNull(panel.Find(".dry-dock-form-card"));
    }

    [Fact]
    public void The_schedule_form_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".dry-dock-schedule-toggle"));
    }

    [Fact]
    public void The_port_filter_lists_the_ports_from_the_directory()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            panel.FindAll("#dry-dock-port option"),
            option => option.TextContent.Contains("Rotterdam", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_port_filter_is_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(
            () => Assert.True(panel.FindAll("#dry-dock-port option").Count > 1));

        panel.Find("#dry-dock-port").Change(PortId);

        panel.WaitForAssertion(() => Assert.Contains(
            maintenanceUris,
            uri => uri.Query.Contains($"portId={PortId}", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_port_picker_asks_only_for_ports_that_carry_a_shipyard()
    {
        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        panel.FindAll(".port-picker-input")[0].Focus();

        panel.WaitForAssertion(() => Assert.Contains(
            voyagesUris,
            uri => uri.AbsolutePath.EndsWith("/ports", StringComparison.Ordinal)
                && uri.Query.Contains("shipyardsOnly=true", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_port_picker_shows_the_shipyard_beside_the_port()
    {
        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        panel.FindAll(".port-picker-input")[0].Focus();

        panel.WaitForAssertion(() =>
        {
            var option = panel.FindAll(".port-picker-option")[0];

            Assert.Contains("Rotterdam", option.TextContent, StringComparison.Ordinal);
            Assert.Contains("Damen", option.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_dry_dock_without_a_port_is_not_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        maintenanceBodies.Clear();

        panel.Find(".dry-dock-form").Submit();

        Assert.Empty(maintenanceBodies);
        Assert.Contains(
            "Port",
            panel.Find(".dry-dock-field-errors").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduling_posts_the_chosen_vessel_port_date_and_tasks()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{DryDockId}}"}""")
            : Json(HttpStatusCode.OK, DryDockPage(DryDock()));

        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        SelectVesselAndPort(panel);
        panel.Find("#dry-dock-start-date").Change("2026-09-01");
        panel.Find("#dry-dock-planned-tasks").Change("  Hull coating  ");
        panel.Find(".dry-dock-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var body = Assert.Single(maintenanceBodies);

            Assert.Contains(VesselId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(PortId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2026-09-01", body, StringComparison.Ordinal);
            Assert.Contains("Hull coating", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_successful_schedule_closes_the_form_and_refreshes_the_board()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{DryDockId}}"}""")
            : Json(HttpStatusCode.OK, DryDockPage(DryDock()));

        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        SelectVesselAndPort(panel);
        panel.Find("#dry-dock-start-date").Change("2026-09-01");
        panel.Find("#dry-dock-planned-tasks").Change("Hull coating");
        maintenanceUris.Clear();
        panel.Find(".dry-dock-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".dry-dock-form-card"));
            Assert.Contains(maintenanceUris, uri => uri.Query.Contains("pageNumber=1", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_rejected_schedule_reports_the_field_errors_and_keeps_the_form_open()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"startDate":["Required."]}}""")
            : Json(HttpStatusCode.OK, DryDockPage(DryDock()));

        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        SelectVesselAndPort(panel);
        panel.Find("#dry-dock-start-date").Change("2026-09-01");
        panel.Find("#dry-dock-planned-tasks").Change("Hull coating");
        panel.Find(".dry-dock-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.NotNull(panel.Find(".dry-dock-form-card"));
            Assert.Contains(
                "Start date",
                panel.Find(".dry-dock-field-errors").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_unreachable_api_reports_the_schedule_failure()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("boom")
            : Json(HttpStatusCode.OK, DryDockPage(DryDock()));

        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        SelectVesselAndPort(panel);
        panel.Find("#dry-dock-start-date").Change("2026-09-01");
        panel.Find("#dry-dock-planned-tasks").Change("Hull coating");
        panel.Find(".dry-dock-form").Submit();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".dry-dock-inline-error")));
    }

    [Fact]
    public void Cancelling_the_schedule_form_closes_it()
    {
        var panel = RenderPanel();

        panel.Find(".dry-dock-schedule-toggle").Click();
        panel.Find(".dry-dock-form .maintenance-reset").Click();

        Assert.Empty(panel.FindAll(".dry-dock-form-card"));
    }

    [Fact]
    public void An_unreachable_board_offers_a_retry()
    {
        respondToMaintenance = _ => throw new HttpRequestException("boom");

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".api-error-state")));
    }

    [Fact]
    public void An_empty_board_explains_the_filters()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, DryDockPage());

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".empty-state")));
    }

    private static void SelectVesselAndPort(IRenderedComponent<DryDocksPanel> panel)
    {
        panel.FindAll(".vessel-picker-input")[1].Focus();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".vessel-picker-option")));
        panel.FindAll(".vessel-picker-option")[0].Click();

        panel.FindAll(".port-picker-input")[0].Focus();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".port-picker-option")));
        panel.FindAll(".port-picker-option")[0].Click();
    }

    private IRenderedComponent<DryDocksPanel> RenderPanel(params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            maintenanceUris.Add(request.RequestUri!);

            if (request.Content is not null)
            {
                maintenanceBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return await respondToMaintenance(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")
                : Json(
                    HttpStatusCode.OK,
                    $$"""
                    {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"9074729",
                    "flagState":"Norway","vesselType":"Container","status":"Active"}],
                    "pageNumber":1,"pageSize":10,"totalCount":1}
                    """)))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var voyagesHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            voyagesUris.Add(request.RequestUri!);

            return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
                ? respondToPortLookup(request)
                : respondToPortSearch(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(new VoyagesApiClient(voyagesHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        if (boardUrl is not null)
        {
            // A query string can only reach the panel through the address bar.
            Services.GetRequiredService<NavigationManager>().NavigateTo(boardUrl);
        }

        var panel = Render<DryDocksPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(maintenanceUris));

        return panel;
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

    private static string DryDock(
        string startDate = "2026-09-01",
        string endDate = "null",
        string plannedTasks = "Hull coating",
        string actualCost = "null",
        string status = "Planned",
        string isOverdue = "false",
        string daysUntilStart = "12")
    {
        return $$"""
            {"dryDockId":"{{DryDockId}}","vesselId":"{{VesselId}}","portId":"{{PortId}}",
            "startDate":"{{startDate}}","endDate":{{endDate}},"plannedTasks":"{{plannedTasks}}",
            "actualCost":{{actualCost}},"status":"{{status}}","isOverdue":{{isOverdue}},
            "daysUntilStart":{{daysUntilStart}}}
            """;
    }

    private static string DryDockPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":25,"totalCount":{{items.Length}}}
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
