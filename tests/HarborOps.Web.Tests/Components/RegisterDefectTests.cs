using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class RegisterDefectTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000001";
    private const string EquipmentId = "50000000-0000-0000-0000-000000000002";
    private const string DefectId = "60000000-0000-0000-0000-000000000003";

    private readonly List<HttpRequestMessage> maintenanceRequests = [];
    private readonly List<string> maintenanceBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance = request =>
        IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(HttpStatusCode.Created, Defect());

    [Fact]
    public void A_pre_supplied_vessel_is_shown_as_static_text_with_no_picker()
    {
        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() => Assert.Equal(
            "Aurora Borealis",
            page.Find("[aria-labelledby='defect-vessel-label']").TextContent.Trim()));

        Assert.Empty(page.FindAll(".vessel-picker-input"));
    }

    [Fact]
    public void No_pre_supplied_vessel_renders_the_picker_for_selection()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(".vessel-picker-input"));
        Assert.Empty(page.FindAll("[aria-labelledby='defect-vessel-label'].maintenance-title"));
    }

    [Fact]
    public void Equipment_options_are_scoped_to_the_selected_vessel_and_offer_no_equipment()
    {
        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() =>
        {
            var request = Assert.Single(maintenanceRequests, IsEquipmentRequest);

            Assert.Contains($"vesselId={VesselId}", request.RequestUri!.Query, StringComparison.Ordinal);

            var options = page.FindAll("#defect-equipment option");

            Assert.Equal("No specific equipment", options[0].TextContent.Trim());
            Assert.Contains(options, option => option.TextContent.Trim() == "Main engine");
        });
    }

    [Fact]
    public void The_submit_button_is_disabled_and_nothing_is_sent_while_the_title_is_missing()
    {
        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#defect-equipment option")));

        page.Find("#defect-severity").Change("High");
        page.Find("#defect-observed").Change("2026-08-20T10:00:00");

        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));

        page.Find(".maintenance-form").Submit();

        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_successful_submit_shows_the_success_banner_with_a_link_and_does_not_navigate()
    {
        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#defect-equipment option")));

        page.Find("#defect-title").Change("Cracked bracket");
        page.Find("#defect-severity").Change("High");
        page.Find("#defect-observed").Change("2026-08-20T10:00:00");

        var navigation = Services.GetRequiredService<NavigationManager>();
        var uriBeforeSubmit = navigation.Uri;

        page.Find(".maintenance-form").Submit();

        Assert.Equal(uriBeforeSubmit, navigation.Uri);

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Defect reported successfully.",
                page.Find(".maintenance-toast").TextContent,
                StringComparison.Ordinal);

            var link = page.Find(".maintenance-form-actions a.maintenance-button");

            Assert.Equal("View defect details", link.TextContent.Trim());
            Assert.Equal($"/maintenance/defects/{DefectId}", link.GetAttribute("href"));
        });

        Assert.Equal(uriBeforeSubmit, navigation.Uri);
    }

    [Fact]
    public void A_validation_response_is_shown_next_to_the_offending_field()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"title":["Title is required."]}}""");

        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#defect-equipment option")));

        page.Find("#defect-title").Change("Cracked bracket");
        page.Find("#defect-severity").Change("High");
        page.Find("#defect-observed").Change("2026-08-20T10:00:00");
        page.Find(".maintenance-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            page.FindAll(".maintenance-inline-error").Select(element => element.TextContent),
            message => message.Contains("Title is required.", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_not_found_response_shows_a_general_error_banner()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(HttpStatusCode.NotFound, """{"title":"Vessel not found.","detail":"The vessel is gone."}""");

        var page = SubmitValidForm();

        page.WaitForAssertion(() => Assert.Contains(
            "The vessel is gone.",
            page.Find(".maintenance-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_forbidden_response_shows_a_general_error_banner()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(HttpStatusCode.Forbidden, "{}");

        var page = SubmitValidForm();

        page.WaitForAssertion(() => Assert.Contains(
            "Your account does not have permission to access maintenance work orders.",
            page.Find(".maintenance-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreachable_api_shows_a_general_error_banner()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : throw new HttpRequestException("boom");

        var page = SubmitValidForm();

        page.WaitForAssertion(() => Assert.Contains(
            "The maintenance service cannot be reached right now.",
            page.Find(".maintenance-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_form_is_hidden_without_the_manage_permission()
    {
        var page = RenderPage(vesselId: VesselId, permissions: "maintenance.read");

        Assert.Empty(page.FindAll("form.maintenance-form"));
        Assert.Contains(
            "does not have permission to report maintenance defects",
            page.Find(".register-defect-denied").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_is_fully_interactive_with_the_manage_permission()
    {
        var page = RenderPage(vesselId: VesselId, permissions: "maintenance.manage");

        Assert.NotNull(page.Find("form.maintenance-form"));
        Assert.False(page.Find("#defect-title").HasAttribute("disabled"));
    }

    [Fact]
    public void The_cancel_link_targets_the_vessel_page_when_a_vessel_was_supplied()
    {
        var page = RenderPage(vesselId: VesselId);

        Assert.Equal($"/fleet/{VesselId}", page.Find(".maintenance-form-actions a.maintenance-reset").GetAttribute("href"));
    }

    [Fact]
    public void The_cancel_link_targets_maintenance_when_no_vessel_was_supplied()
    {
        var page = RenderPage();

        Assert.Equal("/maintenance", page.Find(".maintenance-form-actions a.maintenance-reset").GetAttribute("href"));
    }

    private IRenderedComponent<RegisterDefect> SubmitValidForm()
    {
        var page = RenderPage(vesselId: VesselId);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#defect-equipment option")));

        page.Find("#defect-title").Change("Cracked bracket");
        page.Find("#defect-severity").Change("High");
        page.Find("#defect-observed").Change("2026-08-20T10:00:00");
        page.Find(".maintenance-form").Submit();

        return page;
    }

    private IRenderedComponent<RegisterDefect> RenderPage(string? vesselId = null, params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            maintenanceRequests.Add(request);

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
            IsVesselLookup(request)
                ? Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")
                : Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        Services.GetRequiredService<NavigationManager>().NavigateTo(vesselId is null
            ? "/maintenance/defects/report"
            : $"/maintenance/defects/report?vesselId={vesselId}");

        return Render<RegisterDefect>();
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

    private static bool IsEquipmentRequest(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath == "/api/maintenance/equipment";
    }

    private static bool IsVesselLookup(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal);
    }

    private static string Equipment(
        string equipmentId = EquipmentId,
        string name = "Main engine",
        string category = "Propulsion")
    {
        return $$"""
            {"equipmentId":"{{equipmentId}}","vesselId":"{{VesselId}}","name":"{{name}}",
            "category":"{{category}}","manufacturer":"MAN","serialNumber":"SN-1"}
            """;
    }

    private static string EquipmentPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":100,"totalCount":{{items.Length}}}
            """;
    }

    private static string Defect()
    {
        return $$"""
            {"id":"{{DefectId}}","vesselId":"{{VesselId}}","equipmentId":null,
            "title":"Cracked bracket","description":null,"reportedSeverity":"High",
            "observedAtUtc":"2026-08-20T10:00:00Z","status":"Reported",
            "reportedByUserId":"70000000-0000-0000-0000-000000000004",
            "reportedAtUtc":"2026-08-20T10:05:00Z","confirmedSeverity":null,"priority":null,
            "triageNotes":null,"triagedByUserId":null,"triagedAtUtc":null,"rejectionReason":null,
            "rejectedByUserId":null,"rejectedAtUtc":null,"workOrderId":null,
            "convertedByUserId":null,"convertedAtUtc":null}
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
