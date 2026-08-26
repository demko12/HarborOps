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
    [Fact]
    public void The_workspace_offers_the_board_and_the_three_master_data_directories()
    {
        var page = RenderWorkspace();

        Assert.Equal(
            ["Voyage board", "Ports", "Berths", "Routes"],
            page.FindAll(".voyages-tabs .workspace-tab").Select(tab => tab.TextContent));
    }

    [Fact]
    public void The_voyage_board_is_the_default_tab()
    {
        var page = RenderWorkspace();

        Assert.NotNull(page.Find(".voyages-tabpanel"));
        Assert.Empty(page.FindAll(".ports-panel"));
        Assert.Empty(page.FindAll(".berths-panel"));
        Assert.Empty(page.FindAll(".routes-panel"));
        Assert.Equal("true", page.FindAll(".workspace-tab")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_ports_tab_shows_the_port_directory()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".ports-panel"));
            Assert.Empty(page.FindAll(".voyages-tabpanel"));
            Assert.Equal("true", page.FindAll(".workspace-tab")[1].GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void The_berths_tab_shows_the_berth_directory()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[2].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".berths-panel"));
            Assert.Empty(page.FindAll(".voyages-tabpanel"));
        });
    }

    [Fact]
    public void The_routes_tab_shows_the_route_directory()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[3].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".routes-panel"));
            Assert.Empty(page.FindAll(".voyages-tabpanel"));
        });
    }

    [Fact]
    public void Returning_from_the_register_route_page_lands_on_the_route_directory()
    {
        var page = RenderWorkspace(Voyages.RoutesTabUrl);

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".routes-panel"));
            Assert.Empty(page.FindAll(".voyages-tabpanel"));
            Assert.Equal("true", page.FindAll(".workspace-tab")[3].GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void Returning_to_the_board_hides_the_master_data_directories()
    {
        var page = RenderWorkspace();

        page.FindAll(".workspace-tab")[1].Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".ports-panel")));

        page.FindAll(".workspace-tab")[0].Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".voyages-tabpanel"));
            Assert.Empty(page.FindAll(".ports-panel"));
        });
    }

    private IRenderedComponent<Voyages> RenderWorkspace(string? relativeUrl = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/voyages/ports/lookup"
                ? Json(HttpStatusCode.OK, """{"items":[]}""")
                : Json(HttpStatusCode.OK, EmptyPage())))
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

        if (relativeUrl is not null)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl);
        }

        return Render<Voyages>();
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
