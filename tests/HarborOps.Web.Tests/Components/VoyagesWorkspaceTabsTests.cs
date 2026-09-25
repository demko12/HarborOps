using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class VoyagesWorkspaceTabsTests : BunitContext
{
    private readonly List<string> requestedPaths = [];

    public VoyagesWorkspaceTabsTests()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/voyages/ports/lookup" => Json(HttpStatusCode.OK, """{"items":[]}"""),
                "/api/voyages/master-data-summary" => Json(
                    HttpStatusCode.OK,
                    """{"portCount":60,"berthCount":143,"routeCount":12,"averageRouteDistance":75}"""),
                _ => Json(HttpStatusCode.OK, EmptyPage()),
            };
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.AddSingleton(new FleetApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
            Json(HttpStatusCode.OK, """{"vessels":[]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        }));
        Services.AddSingleton(VoyagesTestAuthorization.Create(["voyages.read", "voyages.manage"]));
    }

    [Fact]
    public void The_workspace_offers_the_board_and_the_three_master_data_directories()
    {
        var page = RenderWorkspace<Voyages>("/voyages");

        Assert.Equal(
            ["Voyage board", "Ports", "Berths", "Routes"],
            page.FindAll(".voyages-tabs .workspace-tab").Select(tab => tab.TextContent));
        Assert.Equal(
            ["/voyages", "/voyages/ports", "/voyages/berths", "/voyages/routes"],
            page.FindAll(".voyages-tabs .workspace-tab").Select(tab => tab.GetAttribute("href")));
    }

    [Fact]
    public void The_voyage_board_shows_no_master_data_directories()
    {
        var page = RenderWorkspace<Voyages>("/voyages");

        Assert.Empty(page.FindAll(".ports-panel"));
        Assert.Empty(page.FindAll(".berths-panel"));
        Assert.Empty(page.FindAll(".routes-panel"));
        Assert.Empty(page.FindAll(".voyages-summary-scoped"));
        Assert.Equal("Voyage board", page.Find(".workspace-tab.active").TextContent);
        Assert.DoesNotContain(requestedPaths, path => path == "/api/voyages/master-data-summary");
    }

    [Fact]
    public void The_ports_page_shows_the_port_directory_and_its_total()
    {
        var page = RenderWorkspace<VoyagesPorts>("/voyages/ports");

        page.WaitForAssertion(() => Assert.Equal(
            "60",
            page.Find(".voyages-summary-scoped .voyages-kpi-value").TextContent.Trim()));
        Assert.NotNull(page.Find(".ports-panel"));
        Assert.Equal("Ports", page.Find(".workspace-tab.active").TextContent);
        Assert.Single(requestedPaths, path => path == "/api/voyages/master-data-summary");
    }

    [Fact]
    public void The_berths_page_shows_the_berth_directory_and_its_total()
    {
        var page = RenderWorkspace<VoyagesBerths>("/voyages/berths");

        page.WaitForAssertion(() => Assert.Equal(
            "143",
            page.Find(".voyages-summary-scoped .voyages-kpi-value").TextContent.Trim()));
        Assert.NotNull(page.Find(".berths-panel"));
        Assert.Equal("Berths", page.Find(".workspace-tab.active").TextContent);
    }

    [Fact]
    public void The_routes_page_shows_the_route_directory_and_its_totals()
    {
        var page = RenderWorkspace<VoyagesRoutes>("/voyages/routes");

        page.WaitForAssertion(() => Assert.Equal(
            ["12", MasterDataFormat.DistanceNm(75m)],
            page.FindAll(".voyages-summary-scoped .voyages-kpi-value").Select(value => value.TextContent.Trim())));
        Assert.NotNull(page.Find(".routes-panel"));
        Assert.Equal("Routes", page.Find(".workspace-tab.active").TextContent);
    }

    [Fact]
    public void Returning_from_the_register_route_page_lands_on_the_route_directory()
    {
        var route = Assert.Single(
            typeof(VoyagesRoutes).GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>());

        Assert.Equal(Voyages.RoutesTabUrl, route.Template);
    }

    [Fact]
    public void Every_workspace_page_shares_the_voyages_styles()
    {
        Assert.True(RenderWorkspace<Voyages>("/voyages").Find(".voyages-page").HasAttribute("b-voyages"));
        Assert.True(RenderWorkspace<VoyagesPorts>("/voyages/ports").Find(".voyages-page").HasAttribute("b-voyages"));
        Assert.True(RenderWorkspace<VoyagesBerths>("/voyages/berths").Find(".voyages-page").HasAttribute("b-voyages"));
        Assert.True(RenderWorkspace<VoyagesRoutes>("/voyages/routes").Find(".voyages-page").HasAttribute("b-voyages"));
    }

    private IRenderedComponent<TPage> RenderWorkspace<TPage>(string relativeUrl)
        where TPage : IComponent
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl);

        return Render<TPage>();
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
