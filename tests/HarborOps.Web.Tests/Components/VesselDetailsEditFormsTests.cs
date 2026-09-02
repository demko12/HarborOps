using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Fleet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class VesselDetailsEditFormsTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000009";
    private const string ReplacementTypeId = "50000000-0000-0000-0000-000000000002";

    private const string RegistryCardEdit = "[aria-labelledby='vessel-identity-title'] .vessel-card-edit";
    private const string SpecsCardEdit = "[aria-labelledby='vessel-specs-title'] .vessel-card-edit";
    private const string RegistryModal = "[aria-labelledby='vessel-registry-form-title']";
    private const string SpecsModal = "[aria-labelledby='vessel-specifications-form-title']";

    private readonly List<(string Method, string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, string, Task<HttpResponseMessage>> onWrite =
        (_, _) => Json(HttpStatusCode.OK, Details());

    [Fact]
    public void A_read_only_operator_sees_no_edit_controls()
    {
        var page = RenderPage(permissions: CurrentUserAuthorizationState.FleetReadPermission);

        Assert.Empty(page.FindAll(".vessel-card-edit"));
        Assert.Empty(page.FindAll(".vessel-edit-modal"));
    }

    [Fact]
    public void A_fleet_manager_gets_an_edit_button_on_the_identity_and_specification_cards()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(RegistryCardEdit));
        Assert.NotNull(page.Find(SpecsCardEdit));
        Assert.Empty(page.FindAll(".vessel-edit-modal"));
    }

    [Fact]
    public void The_identity_card_carries_the_registry_particulars_that_the_form_edits()
    {
        var page = RenderPage();

        var identity = page.Find("[aria-labelledby='vessel-identity-title']").TextContent;

        Assert.Contains("Bar", identity, StringComparison.Ordinal);
        Assert.Contains("Lloyd's Register", identity, StringComparison.Ordinal);
        Assert.Contains("MSMD-42", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_edit_button_opens_a_modal_on_the_current_values_with_the_immutable_fields()
    {
        var page = RenderPage();

        page.Find(RegistryCardEdit).Click();

        var modal = page.Find(".vessel-edit-modal" + RegistryModal);
        Assert.Equal("Northern Star", page.Find("#vessel-registry-name").GetAttribute("value"));
        Assert.Equal("256123000", page.Find("#vessel-registry-mmsi").GetAttribute("value"));

        var immutable = page.Find(".vessel-immutable-facts").TextContent;
        Assert.Contains("5100001", immutable, StringComparison.Ordinal);
        Assert.Contains("60000000-0000-0000-0000-000000000001", immutable, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vessel_type_options_come_from_the_catalogue_endpoint()
    {
        var page = RenderPage();

        page.Find(RegistryCardEdit).Click();

        var options = page.FindAll("#vessel-registry-type option");
        Assert.Contains(options, option => option.TextContent == "Bulk Carrier");
        Assert.Contains(options, option => option.TextContent == "Container Ship");
    }

    [Fact]
    public void Submitting_the_registry_modal_puts_the_editable_fields()
    {
        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();

        page.Find("#vessel-registry-name").Change("Southern Cross");
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);
            Assert.Equal("PUT", write.Method);
            Assert.Equal($"/api/fleet/vessels/{VesselId}/registry-details", write.Path);
            Assert.Contains("\"name\":\"Southern Cross\"", write.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_successful_registry_update_closes_the_modal_confirms_and_reloads_the_vessel()
    {
        var reads = 0;
        RespondToReads(_ =>
        {
            reads++;
            return Json(HttpStatusCode.OK, reads == 1 ? Details() : Details(name: "Southern Cross"));
        });
        onWrite = (_, _) => Json(HttpStatusCode.OK, Details(name: "Southern Cross"));

        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find("#vessel-registry-name").Change("Southern Cross");
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".vessel-edit-modal"));
            Assert.Contains("Registry details updated.", page.Find(".vessel-edit-flash").TextContent, StringComparison.Ordinal);
            Assert.Contains("Southern Cross", page.Find("[aria-labelledby='vessel-identity-title']").TextContent, StringComparison.Ordinal);
            Assert.True(reads >= 2);
        });
    }

    [Fact]
    public void Validation_problems_are_listed_inside_the_modal()
    {
        onWrite = (_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"mmsi":["MMSI must contain digits only."]}}""");

        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find("#vessel-registry-mmsi").Change("25X");
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".vessel-edit-modal"));
            Assert.Contains(
                "MMSI must contain digits only.",
                page.Find(".vessel-edit-modal .vessel-status-alert").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_duplicate_identifier_is_reported_as_a_conflict_inside_the_modal()
    {
        onWrite = (_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"A vessel with this MMSI already exists.","detail":"MMSI is taken.","status":409}""");

        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "A vessel with this MMSI already exists.",
            page.Find(".vessel-edit-modal .vessel-status-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_forbidden_registry_update_explains_the_missing_permission_inside_the_modal()
    {
        onWrite = (_, _) => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Fleet access denied",
            page.Find(".vessel-edit-modal .vessel-status-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_registry_update_can_be_retried_from_the_modal()
    {
        onWrite = (_, _) => Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find("#vessel-registry-name").Change("Southern Cross");
        page.Find(".vessel-edit-modal form.vessel-edit-form").Submit();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".vessel-edit-modal .api-error-state")));

        onWrite = (_, _) => Json(HttpStatusCode.OK, Details(name: "Southern Cross"));
        page.Find(".vessel-edit-modal .api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, writes.Count);
            Assert.Empty(page.FindAll(".vessel-edit-modal"));
            Assert.Contains("Registry details updated.", page.Find(".vessel-edit-flash").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Cancelling_a_dirty_modal_asks_for_confirmation_before_discarding()
    {
        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        page.Find("#vessel-registry-name").Change("Changed");

        page.Find(".vessel-edit-modal .vessel-edit-cancel").Click();
        Assert.Contains("Discard your unsaved registry changes?", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll(".vessel-edit-modal"));

        page.FindAll(".vessel-assignment-confirmation button")[1].Click();
        Assert.DoesNotContain("Discard your unsaved registry changes?", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll(".vessel-edit-modal"));

        page.Find(".vessel-edit-modal .vessel-edit-cancel").Click();
        page.FindAll(".vessel-assignment-confirmation button")[0].Click();
        Assert.Empty(page.FindAll(".vessel-edit-modal"));
        Assert.Empty(writes);
    }

    [Fact]
    public void Cancelling_an_untouched_modal_closes_it_without_a_prompt()
    {
        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();

        page.Find(".vessel-edit-modal .vessel-edit-cancel").Click();

        Assert.Empty(page.FindAll(".vessel-edit-modal"));
        Assert.DoesNotContain("Discard your unsaved", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_close_control_dismisses_an_untouched_modal()
    {
        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();

        page.Find(".vessel-edit-modal-close").Click();

        Assert.Empty(page.FindAll(".vessel-edit-modal"));
    }

    [Fact]
    public void Submitting_the_specifications_modal_puts_the_measurements()
    {
        var page = RenderPage();
        page.Find(SpecsCardEdit).Click();

        page.Find("#vessel-spec-dwt").Change("53000");
        page.Find(SpecsModal + " form.vessel-edit-form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);
            Assert.Equal("PUT", write.Method);
            Assert.Equal($"/api/fleet/vessels/{VesselId}/specifications", write.Path);
            Assert.Contains("\"dwt\":53000", write.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Opening_the_specification_modal_closes_the_registry_modal()
    {
        var page = RenderPage();
        page.Find(RegistryCardEdit).Click();
        Assert.NotEmpty(page.FindAll(RegistryModal));

        page.Find(SpecsCardEdit).Click();

        Assert.Empty(page.FindAll(RegistryModal));
        Assert.NotEmpty(page.FindAll(SpecsModal));
    }

    [Fact]
    public void The_breadcrumb_still_preserves_the_directory_filters()
    {
        var page = RenderPage(returnTo: "/fleet?search=Northern&status=At%20Port&page=2");

        Assert.Equal(
            "/fleet?search=Northern&status=At%20Port&page=2",
            page.Find(".page-breadcrumb a").GetAttribute("href"));
    }

    private Func<HttpRequestMessage, HttpResponseMessage>? readOverride;

    private void RespondToReads(Func<HttpRequestMessage, Task<HttpResponseMessage>> factory) =>
        readOverride = request => factory(request).GetAwaiter().GetResult();

    private IRenderedComponent<VesselDetails> RenderPage(
        string? returnTo = null,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method != HttpMethod.Get)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(CancellationToken.None);
                writes.Add((request.Method.Method, path, body));
                return await onWrite(request, body);
            }

            if (path == "/api/fleet/vessel-types")
            {
                return await Json(HttpStatusCode.OK, VesselTypesJson);
            }

            if (path.EndsWith("/fleet-assignments", StringComparison.Ordinal))
            {
                return await Json(HttpStatusCode.OK, """{"items":[]}""");
            }

            if (path == "/api/fleet/fleets")
            {
                return await Json(HttpStatusCode.OK, """{"items":[],"totalCount":0,"pageNumber":1,"pageSize":100}""");
            }

            return readOverride is not null
                ? readOverride(request)
                : await Json(HttpStatusCode.OK, Details());
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new FleetApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? [CurrentUserAuthorizationState.FleetManagePermission] : permissions));

        var navigation = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        navigation.NavigateTo(returnTo is null
            ? $"/fleet/{VesselId}"
            : $"/fleet/{VesselId}?returnTo={Uri.EscapeDataString(returnTo)}");

        var page = Render<VesselDetails>(parameters => parameters.Add(
            component => component.VesselId,
            Guid.Parse(VesselId)));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".vessel-details-header")));

        return page;
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

    private const string VesselTypesJson = """
        {"vesselTypes":[{"id":"50000000-0000-0000-0000-000000000001","name":"Bulk Carrier","code":"BULK"},
        {"id":"50000000-0000-0000-0000-000000000002","name":"Container Ship","code":"CON"}]}
        """;

    private static string Details(string name = "Northern Star")
    {
        return $$"""
            {"vesselId":"{{VesselId}}","companyId":"60000000-0000-0000-0000-000000000001",
            "name":"{{name}}","imoNumber":"5100001","mmsi":"256123000","callSign":"9HA1234",
            "flagState":"Montenegro","vesselTypeId":"{{ReplacementTypeId}}","vesselTypeName":"Container Ship",
            "dwt":52000,"gt":29000,"loa":190,"beam":32,"draft":12,"portOfRegistry":"Bar",
            "classificationSociety":"Lloyd's Register","lifecycleStage":"In service","msmdId":"MSMD-42",
            "currentStatus":"Active","createdAtUtc":"2026-01-01T08:00:00Z","updatedAtUtc":"2026-08-01T10:00:00Z",
            "fleetId":null,"fleetName":null,"statusHistory":[]}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
