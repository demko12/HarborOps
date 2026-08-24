using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class PortsPanelTests : BunitContext
{
    private const string PortId = "70000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, PortPage(Port()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_directory_lists_the_registered_ports()
    {
        var panel = RenderPanel();

        var cells = panel.Find(".ports-table tbody tr").QuerySelectorAll("td");

        Assert.Equal("Port of Bar", cells[0].TextContent);
        Assert.Equal("Montenegro", cells[1].TextContent);
        Assert.Equal("Bar", cells[2].TextContent);
        Assert.Equal("MEBAR", cells[3].TextContent);
        Assert.Equal("42.0936, 19.0894", cells[4].TextContent);
    }

    [Fact]
    public void Missing_optional_values_render_as_a_placeholder()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            PortPage(Port(city: "null", unLocode: "null", latitude: "null", longitude: "null")));

        var panel = RenderPanel();

        var cells = panel.Find(".ports-table tbody tr").QuerySelectorAll("td");

        Assert.Equal(MasterDataFormat.MissingValue, cells[2].TextContent);
        Assert.Equal(MasterDataFormat.MissingValue, cells[3].TextContent);
        Assert.Equal(MasterDataFormat.MissingValue, cells[4].TextContent);
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#ports-search").Input("Bar");
        panel.Find("#ports-country").Input("Montenegro");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == "/api/voyages/ports").Query;

            Assert.Contains("search=Bar", query, StringComparison.Ordinal);
            Assert.Contains("country=Montenegro", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Registration_posts_the_new_port_and_reloads_the_directory()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{PortId}}"}""")
            : Json(HttpStatusCode.OK, PortPage(Port()));

        var panel = RenderPanel();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find("#port-name").Change("Port of Kotor");
        panel.Find("#port-country").Change("Montenegro");
        panel.Find("#port-un-locode").Change("MEKOT");
        panel.Find(".ports-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "The port was registered successfully.",
                panel.Find(".voyages-action-success").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                requestBodies,
                body => body.Contains("Port of Kotor", StringComparison.Ordinal)
                    && body.Contains("MEKOT", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Registration_shows_the_validation_errors_returned_by_the_api()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"name":["Port name is required."]}}""")
            : Json(HttpStatusCode.OK, PortPage(Port()));

        var panel = RenderPanel();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find(".ports-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "Port name is required.",
            panel.Find(".voyages-modal .voyages-validation").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_shows_a_conflict_returned_by_the_api()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"A port with this UN/LOCODE already exists.","errors":{"unLocode":["Already registered."]}}""")
            : Json(HttpStatusCode.OK, PortPage(Port()));

        var panel = RenderPanel();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find(".ports-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "A port with this UN/LOCODE already exists.",
            panel.Find(".voyages-modal").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_register_form_opens_as_a_modal_dialog()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".voyages-modal-backdrop"));

        panel.Find(".voyages-schedule-button").Click();

        var dialog = panel.Find(".voyages-modal-backdrop .voyages-modal");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
    }

    [Fact]
    public void The_modal_close_button_closes_the_register_form()
    {
        var panel = RenderPanel();

        panel.Find(".voyages-schedule-button").Click();
        panel.Find(".voyages-modal-close").Click();

        Assert.Empty(panel.FindAll(".voyages-modal-backdrop"));
    }

    [Fact]
    public void Required_fields_are_marked_in_the_register_form()
    {
        var panel = RenderPanel();

        panel.Find(".voyages-schedule-button").Click();

        var markedLabels = panel.FindAll(".voyages-modal .required-marker")
            .Select(marker => marker.ParentElement!.TextContent.Trim())
            .ToList();

        Assert.Equal(2, markedLabels.Count);
        Assert.Contains(markedLabels, label => label.StartsWith("Name", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Country", StringComparison.Ordinal));
    }

    [Fact]
    public void The_register_action_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("voyages.read");

        Assert.Empty(panel.FindAll(".voyages-schedule-button"));
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
    public void An_empty_directory_shows_the_empty_state()
    {
        respond = _ => Json(HttpStatusCode.OK, PortPage());

        var panel = RenderPanel();

        Assert.Contains("No ports", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
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
        Render<PortsPanel>();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<PortsPanel> RenderPanel(params string[] permissions)
    {
        RegisterServices(permissions);

        var panel = Render<PortsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

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

    private static string Port(
        string name = "Port of Bar",
        string country = "Montenegro",
        string city = "\"Bar\"",
        string unLocode = "\"MEBAR\"",
        string latitude = "42.0936",
        string longitude = "19.0894")
    {
        return $$"""
            {"portId":"{{PortId}}","name":"{{name}}","country":"{{country}}","city":{{city}},
            "unLocode":{{unLocode}},"latitude":{{latitude}},"longitude":{{longitude}}}
            """;
    }

    private static string PortPage(params string[] items)
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

internal static class VoyagesTestAuthorization
{
    internal static CurrentUserAuthorizationState Create(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(
                CurrentUserAuthorizationState.PermissionClaimType,
                permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }
}
