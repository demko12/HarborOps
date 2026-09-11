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

public sealed class MaintenanceWorkspaceTabsTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";

    [Fact]
    public void The_work_order_board_is_the_default_tab()
    {
        var page = RenderWorkspace();

        Assert.NotNull(page.Find(".maintenance-tabpanel"));
        Assert.Empty(page.FindAll(".equipment-panel"));
        Assert.Empty(page.FindAll(".schedules-panel"));
    }

    [Fact]
    public void The_equipment_tab_shows_the_equipment_registry()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".equipment-panel"));
            Assert.Empty(page.FindAll(".maintenance-tabpanel"));
        });
    }

    [Fact]
    public void The_preventive_schedules_tab_shows_the_schedule_board()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[2].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".schedules-panel"));
            Assert.Empty(page.FindAll(".maintenance-tabpanel"));
        });
    }

    [Fact]
    public void The_tab_in_the_address_opens_the_dry_dock_board()
    {
        var page = RenderWorkspace("/maintenance?tab=dry-docks&status=Planned");

        Assert.NotNull(page.Find(".dry-dock-panel"));
        Assert.Empty(page.FindAll(".maintenance-tabpanel"));
    }

    [Fact]
    public void An_unknown_tab_in_the_address_falls_back_to_the_work_orders()
    {
        var page = RenderWorkspace("/maintenance?tab=nonsense");

        Assert.NotNull(page.Find(".maintenance-tabpanel"));
        Assert.Empty(page.FindAll(".dry-dock-panel"));
    }

    [Fact]
    public void The_purchase_orders_tab_shows_the_procurement_board()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[5].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".procurement-panel"));
            Assert.Empty(page.FindAll(".maintenance-tabpanel"));
        });
    }

    [Fact]
    public void The_suppliers_tab_shows_the_supplier_directory()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[6].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".suppliers-panel"));
            Assert.Empty(page.FindAll(".maintenance-tabpanel"));
        });
    }

    [Fact]
    public void The_preventive_work_tab_shows_the_forecast()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[3].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".preventive-work-panel"));
            Assert.Single(page.FindAll(".maintenance-tabpanel"));
            Assert.Empty(page.FindAll(".schedules-panel"));
        });
    }

    [Fact]
    public void The_tab_in_the_address_opens_the_preventive_work_forecast()
    {
        var page = RenderWorkspace("/maintenance?tab=preventive-work");

        Assert.NotNull(page.Find(".preventive-work-panel"));
        Assert.Empty(page.FindAll(".schedules-panel"));
    }

    private IRenderedComponent<Maintenance> RenderWorkspace(string? workspaceUrl = null)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/maintenance/equipment" => Json(HttpStatusCode.OK, EmptyPage()),
                "/api/maintenance/schedules" => Json(HttpStatusCode.OK, EmptyPage()),
                _ => Json(HttpStatusCode.OK, EmptyPage()),
            }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(
            request => IsVesselLookup(request)
                ? Json(HttpStatusCode.OK, """{"vessels":[]}""")
                : Json(HttpStatusCode.OK, EmptyPage())))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var voyagesHttpClient = new HttpClient(new StubHttpMessageHandler(
            request => IsVesselLookup(request)
                ? Json(HttpStatusCode.OK, """{"ports":[]}""")
                : Json(HttpStatusCode.OK, EmptyPage())))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(new VoyagesApiClient(voyagesHttpClient));
        Services.AddSingleton(CreateAuthorizationState());

        if (workspaceUrl is not null)
        {
            // A query string can only reach the workspace through the address bar.
            Services.GetRequiredService<NavigationManager>().NavigateTo(workspaceUrl);
        }

        return Render<Maintenance>();
    }

    private static CurrentUserAuthorizationState CreateAuthorizationState()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, "maintenance.manage"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private static string EmptyPage()
    {
        return """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""";
    }

    private static bool IsVesselLookup(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
