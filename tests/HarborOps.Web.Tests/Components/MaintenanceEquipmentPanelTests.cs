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

public sealed class MaintenanceEquipmentPanelTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";
    private const string EquipmentId = "c0000000-0000-0000-0000-00000000000c";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, EquipmentPage(Equipment()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    [Fact]
    public void Registry_lists_equipment_with_the_looked_up_vessel_name()
    {
        var panel = RenderPanel();

        var row = panel.Find(".equipment-row");

        Assert.Contains("Main engine", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Propulsion", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("Aurora Borealis", panel.Find(".equipment-vessel").TextContent);
    }

    [Fact]
    public void Missing_optional_values_render_as_a_placeholder()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            EquipmentPage(Equipment(manufacturer: "null", serialNumber: "null")));

        var panel = RenderPanel();

        var cells = panel.Find(".equipment-row").QuerySelectorAll("td");

        Assert.Equal(MaintenanceFormat.MissingValue, cells[2].TextContent);
        Assert.Equal(MaintenanceFormat.MissingValue, cells[3].TextContent);
    }

    [Fact]
    public void Vessel_name_falls_back_to_a_short_reference_when_the_lookup_returns_nothing()
    {
        respondToLookup = _ => Json(HttpStatusCode.OK, """{"vessels":[]}""");

        var panel = RenderPanel();

        Assert.Equal(
            MaintenanceFormat.ShortVesselReference(Guid.Parse(VesselId)),
            panel.Find(".equipment-vessel").TextContent);
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#equipment-category").Input("Propulsion");
        panel.Find("#equipment-manufacturer").Input("Wartsila");
        panel.Find("#equipment-search").Input("SN-1001");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == "/api/maintenance/equipment").Query;

            Assert.Contains("category=Propulsion", query, StringComparison.Ordinal);
            Assert.Contains("manufacturer=Wartsila", query, StringComparison.Ordinal);
            Assert.Contains("search=SN-1001", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Registration_posts_the_new_equipment_and_reloads_the_registry()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{EquipmentId}}"}""")
            : Json(HttpStatusCode.OK, EquipmentPage(Equipment()));

        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();
        panel.Find("#equipment-name").Change("Bow thruster");
        panel.Find("#equipment-category-input").Change("Manoeuvring");
        panel.Find(".equipment-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Equipment was registered successfully.", panel.Find(".equipment-success").TextContent, StringComparison.Ordinal);
            Assert.Contains(requestBodies, body => body.Contains("Bow thruster", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void The_success_toast_disappears_on_its_own()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{EquipmentId}}"}""")
            : Json(HttpStatusCode.OK, EquipmentPage(Equipment()));

        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();
        panel.Find("#equipment-name").Change("Bow thruster");
        panel.Find("#equipment-category-input").Change("Manoeuvring");
        panel.Find(".equipment-form").Submit();

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".maintenance-toast")));

        panel.WaitForAssertion(
            () => Assert.Empty(panel.FindAll(".maintenance-toast")),
            TimeSpan.FromMilliseconds(EquipmentPanel.SuccessToastMilliseconds + 3000));
    }

    [Fact]
    public void Registration_shows_the_validation_errors_returned_by_the_api()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"name":["Name is required."]}}""")
            : Json(HttpStatusCode.OK, EquipmentPage(Equipment()));

        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();
        panel.Find(".equipment-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".equipment-field-errors");

            Assert.Contains("Please correct the following before saving", errors.TextContent, StringComparison.Ordinal);
            Assert.Contains("Name is required.", errors.TextContent, StringComparison.Ordinal);
            Assert.Equal("Name", panel.Find(".equipment-field-errors .maintenance-error-field").TextContent);
        });
    }

    [Fact]
    public void Required_fields_are_marked_in_the_register_form()
    {
        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();

        var markedLabels = panel.FindAll(".equipment-form-card .required-marker")
            .Select(marker => marker.ParentElement!.TextContent.Trim())
            .ToList();

        Assert.Equal(3, markedLabels.Count);
        Assert.Contains(markedLabels, label => label.StartsWith("Vessel", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Name", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Category", StringComparison.Ordinal));
    }

    [Fact]
    public void The_registry_uses_the_shared_workspace_layout()
    {
        var panel = RenderPanel();

        Assert.Equal(2, panel.FindAll(".maintenance-summary-card").Count);
        Assert.Equal("Equipment registry", panel.Find(".maintenance-card-heading h2").TextContent);
        Assert.NotNull(panel.Find(".maintenance-filters"));
        Assert.NotNull(panel.Find(".maintenance-table-scroll .maintenance-table"));
        Assert.NotNull(panel.Find(".maintenance-pagination .maintenance-pages"));
    }

    [Fact]
    public void The_register_form_opens_as_a_modal_dialog()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));

        panel.Find(".equipment-register-toggle").Click();

        var dialog = panel.Find(".maintenance-modal-backdrop .equipment-form-card");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
    }

    [Fact]
    public void Clicking_the_backdrop_closes_the_register_modal()
    {
        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();
        panel.Find(".maintenance-modal-backdrop").Click();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));
    }

    [Fact]
    public void The_modal_close_button_closes_the_register_modal()
    {
        var panel = RenderPanel();

        panel.Find(".equipment-register-toggle").Click();
        panel.Find(".maintenance-modal-close").Click();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));
    }

    [Fact]
    public void The_register_action_is_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".equipment-register-toggle"));
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"Maintenance access denied","detail":"Missing permission."}""");

        var panel = RenderPanel();

        Assert.Contains("Maintenance access denied", panel.Find(".api-error-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_registry_shows_the_empty_state()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, EquipmentPage());

        var panel = RenderPanel();

        Assert.Contains("No equipment", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_panel_cancels_the_in_flight_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondToMaintenanceWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderPanel();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondToMaintenanceWithToken;

    private IRenderedComponent<EquipmentPanel> RenderPanel(params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return respondToMaintenanceWithToken is not null
                ? await respondToMaintenanceWithToken(request, token)
                : await respondToMaintenance(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var lookupHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) => respondToLookup(request)))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(
            (_, _) => Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new VesselLookupApiClient(lookupHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var panel = Render<EquipmentPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

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

    private static string Equipment(
        string equipmentId = EquipmentId,
        string name = "Main engine",
        string category = "Propulsion",
        string manufacturer = "\"Wartsila\"",
        string serialNumber = "\"SN-1001\"")
    {
        return $$"""
            {"equipmentId":"{{equipmentId}}","vesselId":"{{VesselId}}","name":"{{name}}",
            "category":"{{category}}","manufacturer":{{manufacturer}},"serialNumber":{{serialNumber}}}
            """;
    }

    private static string EquipmentPage(params string[] items)
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
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
