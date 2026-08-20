using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Fleet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class VesselDetailsPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000009";

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<(string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Details());

    [Fact]
    public void The_vessel_identity_is_listed()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(name: "Northern Star", imoNumber: "5100001"));

        var page = RenderDetailsPage();

        var identity = page.Find("[aria-labelledby='vessel-identity-title']").TextContent;

        Assert.Contains("Northern Star", identity, StringComparison.Ordinal);
        Assert.Contains("5100001", identity, StringComparison.Ordinal);
        Assert.Contains("Montenegro", identity, StringComparison.Ordinal);
        Assert.Contains("Bulk carrier", identity, StringComparison.Ordinal);
        Assert.Contains("9HA1234", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void The_specifications_are_listed()
    {
        var page = RenderDetailsPage();

        var specifications = page.Find("[aria-labelledby='vessel-specs-title']").TextContent;

        Assert.Contains("52000", specifications, StringComparison.Ordinal);
        Assert.Contains("29000", specifications, StringComparison.Ordinal);
        Assert.Contains("Bar", specifications, StringComparison.Ordinal);
        Assert.Contains("Lloyd's Register", specifications, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_measurement_renders_a_placeholder()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(dwt: "null", gt: "null"));

        var page = RenderDetailsPage();

        Assert.Contains("—", page.Find("[aria-labelledby='vessel-specs-title']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_status_badge_carries_the_status_wording_and_its_modifier()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(currentStatus: "At Port"));

        var page = RenderDetailsPage();

        var badge = page.Find(".vessel-details-header .vessel-status");

        Assert.Equal("At Port", badge.TextContent.Trim());
        Assert.Contains("vessel-status-port", badge.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assigned_vessel_names_its_fleet()
    {
        var page = RenderDetailsPage();

        Assert.Contains(
            "Adriatic Fleet",
            page.Find("[aria-labelledby='vessel-assignment-title']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_unassigned_vessel_is_named_rather_than_left_blank()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(fleetId: "null", fleetName: "null"));

        var page = RenderDetailsPage();

        Assert.Contains(
            "Unassigned",
            page.Find("[aria-labelledby='vessel-assignment-title']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_history_is_listed_in_the_order_the_api_returned_it()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(history: string.Join(
            ',',
            HistoryEntry("1", "Maintenance", "2026-08-03T10:00:00Z", "\"Dry dock\""),
            HistoryEntry("2", "At Port", "2026-08-02T10:00:00Z"),
            HistoryEntry("3", "En Route", "2026-08-01T10:00:00Z"))));

        var page = RenderDetailsPage();

        var entries = page.FindAll(".vessel-history-list li");

        Assert.Equal(3, entries.Count);
        Assert.Contains("Maintenance", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Dry dock", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("At Port", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("En Route", entries[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_history_names_who_recorded_each_change()
    {
        var page = RenderDetailsPage();

        Assert.Contains(
            "operator@harborops.test",
            page.Find(".vessel-history-list li").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_vessel_without_history_offers_the_empty_state_instead_of_a_list()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(history: string.Empty));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".vessel-history-list"));
        Assert.Contains("No status changes yet", page.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_vessel_reports_that_it_was_not_found()
    {
        respond = _ => Json(HttpStatusCode.NotFound, "{}");

        var page = RenderDetailsPage();

        Assert.Contains("Vessel not found", page.Find(".empty-state").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_load_failure_reports_the_problem_from_the_response()
    {
        respond = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load vessel details","detail":"The register is offline.","status":500}""");

        var page = RenderDetailsPage();

        var error = page.Find(".api-error-state").TextContent;

        Assert.Contains("Unable to load vessel details", error, StringComparison.Ordinal);
        Assert.Contains("The register is offline.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forbidden_read_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderDetailsPage();

        Assert.Contains(
            "Your account does not have permission to view this vessel.",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_after_a_load_failure_requests_the_vessel_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderDetailsPage();
        var requestsBeforeRetry = requests.Count;

        respond = _ => Json(HttpStatusCode.OK, Details(name: "Northern Star"));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(requests.Count > requestsBeforeRetry);
            Assert.Contains("Northern Star", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Back_navigation_defaults_to_the_directory()
    {
        var page = RenderDetailsPage();

        Assert.Equal("/fleet", page.Find(".vessel-details-back").GetAttribute("href"));
    }

    [Fact]
    public void Back_navigation_preserves_the_directory_filters()
    {
        var page = RenderDetailsPage(returnTo: "/fleet?search=Northern&status=At%20Port&page=2");

        Assert.Equal(
            "/fleet?search=Northern&status=At%20Port&page=2",
            page.Find(".vessel-details-back").GetAttribute("href"));
    }

    [Fact]
    public void Back_navigation_refuses_a_return_address_that_leaves_the_directory()
    {
        var page = RenderDetailsPage(returnTo: "https://elsewhere.test/fleet");

        Assert.Equal("/fleet", page.Find(".vessel-details-back").GetAttribute("href"));
    }

    [Fact]
    public void The_status_form_is_withheld_from_a_read_only_operator()
    {
        var page = RenderDetailsPage(permissions: CurrentUserAuthorizationState.FleetReadPermission);

        Assert.Empty(page.FindAll(".vessel-status-form"));
        Assert.Empty(page.FindAll(".vessel-status-form-card"));
    }

    [Fact]
    public void The_status_form_is_offered_to_a_fleet_manager()
    {
        var page = RenderDetailsPage(permissions: CurrentUserAuthorizationState.FleetManagePermission);

        Assert.NotNull(page.Find(".vessel-status-form"));
        Assert.NotEmpty(page.FindAll("#vessel-status-select option"));
    }

    [Fact]
    public void The_form_opens_on_the_status_the_vessel_already_holds()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(currentStatus: "Maintenance"));

        var page = RenderDetailsPage();

        Assert.Equal("Maintenance", page.Find("#vessel-status-select").GetAttribute("value"));
    }

    [Fact]
    public void Submitting_sends_the_status_and_the_notes_to_the_vessel()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("#vessel-status-notes").Change("Berthed overnight");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);

            Assert.Equal($"/api/fleet/vessels/{VesselId}/status-changes", write.Path);
            Assert.Contains("\"status\":\"At Port\"", write.Body, StringComparison.Ordinal);
            Assert.Contains("\"notes\":\"Berthed overnight\"", write.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Submitting_never_sends_the_author_of_the_change()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.DoesNotContain(
            "changedBy",
            Assert.Single(writes).Body,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Blank_notes_are_left_out_of_the_request()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("#vessel-status-notes").Change("   ");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "\"notes\":null",
            Assert.Single(writes).Body,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_recorded_change_is_confirmed_and_the_vessel_is_read_again()
    {
        var reads = 0;
        respond = request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));
            }

            reads++;
            return Json(HttpStatusCode.OK, reads == 1
                ? Details(currentStatus: "Active")
                : Details(currentStatus: "At Port", history: HistoryEntry("1", "At Port", "2026-08-04T10:00:00Z")));
        };

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Status updated to At Port.", page.Find(".vessel-status-success").TextContent, StringComparison.Ordinal);
            Assert.True(reads >= 2);
            Assert.Equal("At Port", page.Find(".vessel-details-header .vessel-status").TextContent.Trim());
        });
    }

    [Fact]
    public void The_notes_are_cleared_once_the_change_is_recorded()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("#vessel-status-notes").Change("Berthed overnight");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.True(
            string.IsNullOrEmpty(page.Find("#vessel-status-notes").TextContent)));
    }

    [Fact]
    public void Validation_problems_from_the_api_are_listed_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status must be one of: Active, En Route, At Port, Maintenance, Inactive."]}}""");

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Status must be one of",
            page.Find(".vessel-status-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_unchanged_status_is_reported_as_a_conflict()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(
                HttpStatusCode.Conflict,
                """{"title":"Vessel status is unchanged.","detail":"The vessel already has this status.","status":409}""");

        var page = RenderDetailsPage();

        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() =>
        {
            var alert = page.Find(".vessel-status-alert").TextContent;

            Assert.Contains("Vessel status is unchanged.", alert, StringComparison.Ordinal);
            Assert.Contains("The vessel already has this status.", alert, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_change_refused_by_the_api_explains_the_missing_permission()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Your account does not have permission to change vessel status.",
            page.Find(".vessel-status-alert").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_vessel_that_disappears_mid_edit_is_reported_as_missing()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.NotFound, """{"title":"Vessel not found.","status":404}""");

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Vessel not found",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_change_can_be_retried()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details())
            : Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderDetailsPage();

        page.Find("#vessel-status-select").Change("At Port");
        page.Find("form.vessel-status-form").Submit();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".vessel-status-form-card .api-error-state")));

        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, Details(currentStatus: "At Port"))
            : Json(HttpStatusCode.Created, StatusChangeAccepted("At Port"));

        page.Find(".vessel-status-form-card .api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, writes.Count);
            Assert.Contains("Status updated to At Port.", page.Find(".vessel-status-success").TextContent, StringComparison.Ordinal);
        });
    }

    private IRenderedComponent<VesselDetails> RenderDetailsPage(
        string? returnTo = null,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request);

            if (request.Method != HttpMethod.Get)
            {
                writes.Add((
                    request.RequestUri!.AbsolutePath,
                    request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            }

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new FleetApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? [CurrentUserAuthorizationState.FleetManagePermission] : permissions));

        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(returnTo is null
            ? $"/fleet/{VesselId}"
            : $"/fleet/{VesselId}?returnTo={Uri.EscapeDataString(returnTo)}");

        var page = Render<VesselDetails>(parameters => parameters.Add(
            component => component.VesselId,
            Guid.Parse(VesselId)));

        page.WaitForAssertion(() => Assert.NotEmpty(requests));

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

    private static string HistoryEntry(
        string suffix,
        string status,
        string changedAtUtc,
        string notes = "null",
        string changedBy = "operator@harborops.test")
    {
        return $$"""
            {"id":"8000000{{suffix}}-0000-0000-0000-000000000001","status":"{{status}}",
            "changedBy":"{{changedBy}}","notes":{{notes}},"changedAtUtc":"{{changedAtUtc}}"}
            """;
    }

    private static string Details(
        string name = "Northern Star",
        string imoNumber = "5100001",
        string currentStatus = "Active",
        string dwt = "52000",
        string gt = "29000",
        string fleetId = "\"70000000-0000-0000-0000-000000000001\"",
        string fleetName = "\"Adriatic Fleet\"",
        string? history = null)
    {
        history ??= HistoryEntry("1", "Active", "2026-08-01T10:00:00Z");

        return $$"""
            {"vesselId":"{{VesselId}}","companyId":"60000000-0000-0000-0000-000000000001",
            "name":"{{name}}","imoNumber":"{{imoNumber}}","mmsi":"256123000","callSign":"9HA1234",
            "flagState":"Montenegro","vesselTypeId":"50000000-0000-0000-0000-000000000001",
            "vesselTypeName":"Bulk carrier","dwt":{{dwt}},"gt":{{gt}},"loa":190,"beam":32,"draft":12,
            "portOfRegistry":"Bar","classificationSociety":"Lloyd's Register","lifecycleStage":"In service",
            "currentStatus":"{{currentStatus}}","createdAtUtc":"2026-01-01T08:00:00Z",
            "updatedAtUtc":"2026-08-01T10:00:00Z","fleetId":{{fleetId}},"fleetName":{{fleetName}},
            "statusHistory":[{{history}}]}
            """;
    }

    private static string StatusChangeAccepted(string status)
    {
        return $$"""
            {"id":"90000000-0000-0000-0000-000000000001","vesselId":"{{VesselId}}",
            "status":"{{status}}","changedBy":"operator@harborops.test","notes":null,
            "changedAtUtc":"2026-08-04T10:00:00Z"}
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
