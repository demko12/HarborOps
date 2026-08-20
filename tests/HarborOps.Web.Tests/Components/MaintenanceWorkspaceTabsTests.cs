using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
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

        page.FindAll(".maintenance-tab")[1].Click();

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

        page.FindAll(".maintenance-tab")[2].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".schedules-panel"));
            Assert.Empty(page.FindAll(".maintenance-tabpanel"));
        });
    }

    private IRenderedComponent<Maintenance> RenderWorkspace()
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

        var lookupHttpClient = new HttpClient(new StubHttpMessageHandler(
            _ => Json(HttpStatusCode.OK, """{"vessels":[]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(
            _ => Json(HttpStatusCode.OK, EmptyPage())))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new VesselLookupApiClient(lookupHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState());

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
