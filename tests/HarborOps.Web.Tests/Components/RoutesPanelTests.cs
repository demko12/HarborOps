using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class RoutesPanelTests : BunitContext
{
    private const string BarId = "70000000-0000-0000-0000-000000000001";
    private const string KotorId = "70000000-0000-0000-0000-000000000002";
    private const string RouteId = "90000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, RoutePage(Route()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_directory_lists_the_route_summaries()
    {
        var panel = RenderPanel();

        var cells = panel.Find(".routes-table tbody tr").QuerySelectorAll("td");

        Assert.Equal("Adriatic feeder", cells[0].TextContent);
        Assert.Equal("Bar to Kotor", cells[1].TextContent);
        Assert.Equal("2", cells[2].TextContent);
        Assert.Equal("60", cells[3].TextContent);
        Assert.Equal("300", cells[4].TextContent);
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#routes-search").Input("Adriatic");

        panel.WaitForAssertion(() => Assert.Contains(
            "search=Adriatic",
            requestedUris.Last(uri => uri.AbsolutePath == "/api/voyages/routes").Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Viewing_a_route_lists_its_legs_in_sailing_order()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"/api/voyages/routes/{RouteId}"
            ? Json(HttpStatusCode.OK, RouteDetails())
            : Json(HttpStatusCode.OK, RoutePage(Route()));

        var panel = RenderPanel();

        panel.Find(".routes-table .voyages-row-action").Click();

        panel.WaitForAssertion(() =>
        {
            var rows = panel.FindAll(".route-legs-table tbody tr");

            Assert.Equal(2, rows.Count);
            Assert.Equal("1", rows[0].QuerySelectorAll("td")[0].TextContent);
            Assert.Equal("Port of Bar (MEBAR)", rows[0].QuerySelectorAll("td")[1].TextContent);
            Assert.Equal("0", rows[0].QuerySelectorAll("td")[3].TextContent);
            Assert.Equal("Port of Kotor (MEKOT)", rows[1].QuerySelectorAll("td")[1].TextContent);
            Assert.Equal("60", rows[1].QuerySelectorAll("td")[3].TextContent);
            Assert.Equal("300", rows[1].QuerySelectorAll("td")[4].TextContent);
        });
    }

    [Fact]
    public void Closing_the_legs_hides_the_details_card()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"/api/voyages/routes/{RouteId}"
            ? Json(HttpStatusCode.OK, RouteDetails())
            : Json(HttpStatusCode.OK, RoutePage(Route()));

        var panel = RenderPanel();

        panel.Find(".routes-table .voyages-row-action").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".route-details-card")));

        panel.Find(".route-details-card .voyages-reset").Click();

        Assert.Empty(panel.FindAll(".route-details-card"));
    }

    [Fact]
    public void A_failed_details_load_shows_a_retryable_error()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"/api/voyages/routes/{RouteId}"
            ? Json(
                HttpStatusCode.NotFound,
                """{"title":"The requested route was not found.","detail":"Route was not found."}""")
            : Json(HttpStatusCode.OK, RoutePage(Route()));

        var panel = RenderPanel();

        panel.Find(".routes-table .voyages-row-action").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "The requested route was not found.",
            panel.Find(".route-details-card .api-error-state").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Registering_a_route_opens_its_own_page()
    {
        var panel = RenderPanel();

        Assert.Equal(
            "/voyages/routes/register",
            panel.Find(".voyages-schedule-button").GetAttribute("href"));
    }

    [Fact]
    public void The_register_action_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("voyages.read");

        Assert.Empty(panel.FindAll(".voyages-schedule-button"));
    }

    [Fact]
    public void An_empty_directory_shows_the_empty_state()
    {
        respond = _ => Json(HttpStatusCode.OK, RoutePage());

        var panel = RenderPanel();

        Assert.Contains("No routes", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respond = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"You do not have access to voyages","detail":"Missing permission."}""");

        var panel = RenderPanel();

        Assert.Contains(
            "You do not have access to voyages",
            panel.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_panel_cancels_the_in_flight_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RegisterServices();
        Render<RoutesPanel>();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<RoutesPanel> RenderPanel(params string[] permissions)
    {
        RegisterServices(permissions);

        var panel = Render<RoutesPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return panel;
    }

    private void RegisterServices(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondWithToken is not null
                ? await respondWithToken(request, token)
                : await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.AddSingleton(VoyagesTestAuthorization.Create(
            permissions.Length == 0 ? ["voyages.manage"] : permissions));
    }

    private static string Route(
        string name = "Adriatic feeder",
        string description = "\"Bar to Kotor\"",
        string estimatedDistance = "60",
        string estimatedDuration = "300",
        string stopCount = "2")
    {
        return $$"""
            {"routeId":"{{RouteId}}","name":"{{name}}","description":{{description}},
            "estimatedDistance":{{estimatedDistance}},"estimatedDuration":{{estimatedDuration}},
            "stopCount":{{stopCount}}}
            """;
    }

    private static string RoutePage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":25,"totalCount":{{items.Length}}}
            """;
    }

    private static string RouteDetails()
    {
        return $$"""
            {"routeId":"{{RouteId}}","name":"Adriatic feeder","description":"Bar to Kotor",
            "estimatedDistance":60,"estimatedDuration":300,
            "stops":[
            {"portId":"{{BarId}}","portName":"Port of Bar","portCountry":"Montenegro","unLocode":"MEBAR",
            "stopOrder":1,"distanceFromPrevious":0,"estimatedDurationFromPrevious":0},
            {"portId":"{{KotorId}}","portName":"Port of Kotor","portCountry":"Montenegro","unLocode":"MEKOT",
            "stopOrder":2,"distanceFromPrevious":60,"estimatedDurationFromPrevious":300}]}
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
