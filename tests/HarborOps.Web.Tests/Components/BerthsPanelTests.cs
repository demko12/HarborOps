using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class BerthsPanelTests : BunitContext
{
    private const string BarId = "70000000-0000-0000-0000-000000000001";
    private const string KotorId = "70000000-0000-0000-0000-000000000002";
    private const string BerthId = "80000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = request =>
        IsPortLookup(request)
            ? Json(HttpStatusCode.OK, PortLookup())
            : Json(HttpStatusCode.OK, BerthPage(Berth()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void No_port_is_selected_by_default()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.Find("#berths-port").QuerySelectorAll("option")));

        Assert.Contains("Select a port", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".berths-table"));
        Assert.DoesNotContain(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/berths");
    }

    [Fact]
    public void Selecting_a_port_lists_its_berths()
    {
        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() =>
        {
            var cells = panel.Find(".berths-table tbody tr").QuerySelectorAll("td");

            Assert.Equal("Berth 1", cells[0].TextContent);
            Assert.Equal("210 m · 12 m draft", cells[1].TextContent);
            Assert.Equal("Available", cells[2].TextContent);
        });

        Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/berths"
                && uri.Query.Contains($"portId={BarId}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_port_selector_is_filled_from_the_bounded_lookup()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            var options = panel.Find("#berths-port").QuerySelectorAll("option");

            Assert.Equal(3, options.Length);
            Assert.Equal("Port of Bar (MEBAR)", options[1].TextContent);
            Assert.Equal("Port of Kotor (MEKOT)", options[2].TextContent);
        });

        Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/ports/lookup"
                && uri.Query.Contains("take=50", StringComparison.Ordinal));
    }

    [Fact]
    public void Choosing_another_port_reloads_its_berths()
    {
        var panel = RenderPanelWithPortSelected();

        panel.Find("#berths-port").Change(KotorId);

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/berths"
                && uri.Query.Contains($"portId={KotorId}", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Searching_ports_calls_the_lookup_again()
    {
        var panel = RenderPanel();

        panel.Find("#berths-port-search").Input("Kotor");

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/ports/lookup"
                && uri.Query.Contains("search=Kotor", StringComparison.Ordinal)));
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanelWithPortSelected();

        panel.Find("#berths-search").Input("Tanker");
        panel.Find("#berths-status").Change("Out of service");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == "/api/voyages/berths").Query;

            Assert.Contains("search=Tanker", query, StringComparison.Ordinal);
            Assert.Contains("status=Out%20of%20service", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_status_column_renders_the_status_as_a_pill()
    {
        respond = request => IsPortLookup(request)
            ? Json(HttpStatusCode.OK, PortLookup())
            : Json(HttpStatusCode.OK, BerthPage(Berth(status: "Out of service")));

        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() =>
        {
            var status = panel.Find(".berths-table tbody tr .berth-status");

            Assert.Equal("Out of service", status.TextContent);
            Assert.Contains("out-of-service", status.ClassName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Both_status_pickers_offer_the_four_statuses_in_circulation()
    {
        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".voyages-schedule-button")));

        Assert.Equal(
            ["All statuses", "Available", "Occupied", "Reserved", "Out of service"],
            panel.Find("#berths-status").QuerySelectorAll("option").Select(option => option.TextContent));

        panel.Find(".voyages-schedule-button").Click();

        Assert.Equal(
            ["Select a status", "Available", "Occupied", "Reserved", "Out of service"],
            panel.Find("#berth-status").QuerySelectorAll("option").Select(option => option.TextContent));
    }

    [Fact]
    public void The_dialog_can_register_into_another_port_and_the_directory_follows()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{BerthId}}"}""")
            : IsPortLookup(request)
                ? Json(HttpStatusCode.OK, PortLookup())
                : Json(HttpStatusCode.OK, BerthPage(Berth()));

        var panel = RenderPanelWithPortSelected();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find("#berth-port").Change(KotorId);
        panel.Find("#berth-name").Change("Berth 9");
        panel.Find("#berth-status").Change("Available");
        panel.Find("#berth-length").Change("150");
        panel.Find("#berth-max-draft").Change("8");
        panel.Find(".berths-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestBodies,
                body => body.Contains(KotorId, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                requestedUris,
                uri => uri.AbsolutePath == "/api/voyages/berths"
                    && uri.Query.Contains($"portId={KotorId}", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(KotorId, panel.Find("#berths-port").GetAttribute("value"));
        });
    }

    [Fact]
    public void The_register_action_is_offered_for_the_selected_port()
    {
        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() => Assert.Contains(
            "Register berth",
            panel.Find(".voyages-schedule-button").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_posts_the_berth_under_the_selected_port()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{BerthId}}"}""")
            : IsPortLookup(request)
                ? Json(HttpStatusCode.OK, PortLookup())
                : Json(HttpStatusCode.OK, BerthPage(Berth()));

        var panel = RenderPanelWithPortSelected();

        panel.Find(".voyages-schedule-button").Click();

        Assert.Equal(BarId, panel.Find("#berth-port").GetAttribute("value"));

        panel.Find("#berth-name").Change("Berth 2");
        panel.Find("#berth-status").Change("Available");
        panel.Find("#berth-length").Change("180");
        panel.Find("#berth-max-draft").Change("10");
        panel.Find(".berths-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "The berth was registered successfully.",
                panel.Find(".voyages-action-success").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                requestBodies,
                body => body.Contains("Berth 2", StringComparison.Ordinal)
                    && body.Contains(BarId, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Registration_shows_the_conflict_returned_by_the_api()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"A berth with this name already exists for the port.","errors":{"name":["Already registered."]}}""")
            : IsPortLookup(request)
                ? Json(HttpStatusCode.OK, PortLookup())
                : Json(HttpStatusCode.OK, BerthPage(Berth()));

        var panel = RenderPanelWithPortSelected();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find(".berths-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var alert = panel.Find(".voyages-modal .voyages-action-failure").TextContent;

            Assert.Contains(
                "A berth with this name already exists for the port.",
                alert,
                StringComparison.Ordinal);
            Assert.Contains("Already registered.", alert, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_register_action_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanelWithPortSelected("voyages.read");

        Assert.Empty(panel.FindAll(".voyages-schedule-button"));
    }

    [Fact]
    public void Without_any_port_the_panel_asks_for_a_selection()
    {
        respond = request => IsPortLookup(request)
            ? Json(HttpStatusCode.OK, """{"items":[]}""")
            : Json(HttpStatusCode.OK, BerthPage());

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            "Select a port",
            panel.Find(".empty-state").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_directory_shows_the_empty_state()
    {
        respond = request => IsPortLookup(request)
            ? Json(HttpStatusCode.OK, PortLookup())
            : Json(HttpStatusCode.OK, BerthPage());

        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() => Assert.Contains(
            "No berths",
            panel.Find(".empty-state").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respond = request => IsPortLookup(request)
            ? Json(HttpStatusCode.OK, PortLookup())
            : Json(
                HttpStatusCode.Forbidden,
                """{"title":"You do not have access to voyages","detail":"Missing permission."}""");

        var panel = RenderPanelWithPortSelected();

        panel.WaitForAssertion(() => Assert.Contains(
            "You do not have access to voyages",
            panel.Find(".api-error-state").TextContent,
            StringComparison.Ordinal));
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
        Render<BerthsPanel>();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<BerthsPanel> RenderPanel(params string[] permissions)
    {
        RegisterServices(permissions);

        var panel = Render<BerthsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return panel;
    }

    private IRenderedComponent<BerthsPanel> RenderPanelWithPortSelected(params string[] permissions)
    {
        var panel = RenderPanel(permissions);

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.Find("#berths-port").QuerySelectorAll("option")));
        panel.Find("#berths-port").Change(BarId);

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == "/api/voyages/berths"
                && uri.Query.Contains($"portId={BarId}", StringComparison.OrdinalIgnoreCase)));

        return panel;
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

    private static bool IsPortLookup(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath == "/api/voyages/ports/lookup";
    }

    private static string PortLookup()
    {
        return $$"""
            {"items":[
            {"portId":"{{BarId}}","name":"Port of Bar","country":"Montenegro","unLocode":"MEBAR"},
            {"portId":"{{KotorId}}","name":"Port of Kotor","country":"Montenegro","unLocode":"MEKOT"}]}
            """;
    }

    private static string Berth(
        string name = "Berth 1",
        string length = "210",
        string maxDraft = "12",
        string status = "Available")
    {
        return $$"""
            {"berthId":"{{BerthId}}","portId":"{{BarId}}","name":"{{name}}","length":{{length}},
            "maxDraft":{{maxDraft}},"status":"{{status}}"}
            """;
    }

    private static string BerthPage(params string[] items)
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
