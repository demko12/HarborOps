using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class RegisterRoutePageTests : BunitContext
{
    private const string BarId = "70000000-0000-0000-0000-000000000001";
    private const string KotorId = "70000000-0000-0000-0000-000000000002";
    private const string RouteId = "90000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, PortLookup());

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void Without_the_manage_permission_the_form_is_not_offered()
    {
        var page = RenderPage("voyages.read");

        Assert.Empty(page.FindAll(".register-route-form"));
        Assert.Contains(
            "Voyage management permission required",
            page.Find(".register-route-denied").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_starts_with_two_stops_each_with_its_own_port_picker()
    {
        var page = RenderPage();

        Assert.Equal(2, page.FindAll(".route-builder-stop").Count);
        Assert.Equal(2, page.FindAll(".port-picker-input").Count);
        Assert.NotNull(page.Find("#route-stop-port-0"));
        Assert.NotNull(page.Find("#route-stop-port-1"));
    }

    [Fact]
    public void A_port_picker_browses_ports_over_the_bounded_lookup()
    {
        var page = RenderPage();

        page.Find("#route-stop-port-0").Focus();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".port-picker-option").Count));
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == "/api/voyages/ports/lookup");
    }

    [Fact]
    public void Typing_in_a_port_picker_searches_the_lookup()
    {
        var page = RenderPage();

        page.Find("#route-stop-port-1").Input("Kotor");

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/ports/lookup"
                && uri.Query.Contains("search=Kotor", StringComparison.Ordinal)));
    }

    [Fact]
    public void Each_stop_keeps_its_own_port_selection()
    {
        var page = RenderPage();

        PickPort(page, "route-stop-port-0", "Port of Bar");
        PickPort(page, "route-stop-port-1", "Port of Kotor");

        Assert.Equal("Port of Bar (MEBAR)", page.Find("#route-stop-port-0").GetAttribute("value"));
        Assert.Equal("Port of Kotor (MEKOT)", page.Find("#route-stop-port-1").GetAttribute("value"));
    }

    [Fact]
    public void The_first_stop_cannot_carry_a_leg_from_a_previous_port()
    {
        var page = RenderPage();

        Assert.True(page.Find("#route-stop-distance-0").HasAttribute("disabled"));
        Assert.True(page.Find("#route-stop-duration-0").HasAttribute("disabled"));
        Assert.False(page.Find("#route-stop-distance-1").HasAttribute("disabled"));
    }

    [Fact]
    public void Stops_can_be_added_removed_and_reordered()
    {
        var page = RenderPage();

        page.Find(".route-builder-add").Click();
        Assert.Equal(3, page.FindAll(".route-builder-stop").Count);

        PickPort(page, "route-stop-port-0", "Port of Bar");
        PickPort(page, "route-stop-port-1", "Port of Kotor");

        page.FindAll(".route-builder-stop")[1]
            .QuerySelectorAll(".register-route-step")[0]
            .Click();

        Assert.Equal("Port of Kotor (MEKOT)", page.Find("#route-stop-port-0").GetAttribute("value"));
        Assert.Equal("Port of Bar (MEBAR)", page.Find("#route-stop-port-1").GetAttribute("value"));

        page.FindAll(".route-builder-stop")[2]
            .QuerySelectorAll(".register-route-step")[2]
            .Click();

        Assert.Equal(2, page.FindAll(".route-builder-stop").Count);
    }

    [Fact]
    public void The_last_two_stops_cannot_be_removed()
    {
        var page = RenderPage();

        var removeButtons = page.FindAll(".route-builder-stop")
            .Select(stop => stop.QuerySelectorAll(".register-route-step")[2])
            .ToList();

        Assert.All(removeButtons, button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void The_totals_add_up_the_legs_entered_in_the_builder()
    {
        var page = RenderPage();

        page.Find("#route-stop-distance-1").Change("60.5");
        page.Find("#route-stop-duration-1").Change("300");

        var totals = page.FindAll(".route-builder-totals dd");

        Assert.Equal("60.5 nm", totals[0].TextContent);
        Assert.Equal("300 h", totals[1].TextContent);
    }

    [Fact]
    public void Moving_a_stop_to_the_front_drops_the_leg_it_carried()
    {
        var page = RenderPage();

        page.Find("#route-stop-distance-1").Change("60");
        page.Find("#route-stop-duration-1").Change("300");

        page.FindAll(".route-builder-stop")[1]
            .QuerySelectorAll(".register-route-step")[0]
            .Click();

        var totals = page.FindAll(".route-builder-totals dd");

        Assert.Equal("0 nm", totals[0].TextContent);
        Assert.Equal("0 h", totals[1].TextContent);
    }

    [Fact]
    public void Registering_posts_the_ordered_stops_and_the_computed_totals()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{RouteId}}"}""")
            : Json(HttpStatusCode.OK, PortLookup());

        var page = RenderPage();

        page.Find("#register-route-name").Change("Adriatic feeder");
        PickPort(page, "route-stop-port-0", "Port of Bar");
        PickPort(page, "route-stop-port-1", "Port of Kotor");
        page.Find("#route-stop-distance-1").Change("60");
        page.Find("#route-stop-duration-1").Change("300");
        page.Find(".register-route-form").Submit();

        page.WaitForAssertion(() =>
        {
            var body = requestBodies.Last();

            Assert.Contains("\"estimatedDistance\":60", body, StringComparison.Ordinal);
            Assert.Contains("\"estimatedDuration\":300", body, StringComparison.Ordinal);
            Assert.Contains("\"stopOrder\":1", body, StringComparison.Ordinal);
            Assert.Contains("\"stopOrder\":2", body, StringComparison.Ordinal);
            Assert.Contains(BarId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(KotorId, body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Registering_returns_to_the_route_directory()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{RouteId}}"}""")
            : Json(HttpStatusCode.OK, PortLookup());

        var page = RenderPage();
        var navigation = Services.GetRequiredService<NavigationManager>();

        page.Find(".register-route-form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(
            Voyages.RoutesTabUrl,
            navigation.Uri,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_errors_returned_by_the_api_are_shown()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"stops":["A route must have at least 2 port stops."]}}""")
            : Json(HttpStatusCode.OK, PortLookup());

        var page = RenderPage();

        page.Find(".register-route-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "A route must have at least 2 port stops.",
            page.Find(".register-route-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_stop_port_returned_by_the_api_is_shown()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.NotFound,
                """{"title":"The requested port was not found.","errors":{"stops":["Route stop port was not found."]}}""")
            : Json(HttpStatusCode.OK, PortLookup());

        var page = RenderPage();

        page.Find(".register-route-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Route stop port was not found.",
            page.Find(".register-route-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_port_lookup_is_reported_inside_the_picker()
    {
        respond = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"You do not have access to voyages","detail":"Missing permission."}""");

        var page = RenderPage();

        page.Find("#route-stop-port-0").Focus();

        page.WaitForAssertion(() => Assert.Contains(
            "Unable to search ports right now",
            page.Find(".port-picker-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_page_links_back_to_the_route_directory()
    {
        var page = RenderPage();

        Assert.Equal(
            Voyages.RoutesTabUrl,
            page.Find(".page-breadcrumb a").GetAttribute("href"));
        Assert.Equal(
            Voyages.RoutesTabUrl,
            page.Find(".register-route-actions .register-route-link").GetAttribute("href"));
    }

    [Fact]
    public async Task Disposing_the_page_cancels_the_in_flight_lookup()
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
        var page = Render<RegisterRoute>();
        page.Find("#route-stop-port-0").Focus();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private static void PickPort(IRenderedComponent<RegisterRoute> page, string inputId, string portName)
    {
        page.Find($"#{inputId}").Focus();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".port-picker-option")));

        page.Find($"#{inputId}").ParentElement!
            .QuerySelectorAll(".port-picker-option")
            .First(option => option.TextContent.Contains(portName, StringComparison.Ordinal))
            .Click();
    }

    private IRenderedComponent<RegisterRoute> RenderPage(params string[] permissions)
    {
        RegisterServices(permissions);

        return Render<RegisterRoute>();
    }

    private void RegisterServices(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

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

    private static string PortLookup()
    {
        return $$"""
            {"items":[
            {"portId":"{{BarId}}","name":"Port of Bar","country":"Montenegro","unLocode":"MEBAR"},
            {"portId":"{{KotorId}}","name":"Port of Kotor","country":"Montenegro","unLocode":"MEKOT"}]}
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
