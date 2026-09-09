using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DefectDetailsTests : BunitContext
{
    private const string DefectId = "10000000-0000-0000-0000-000000000001";
    private const string VesselId = "20000000-0000-0000-0000-000000000002";
    private const string WorkOrderId = "30000000-0000-0000-0000-000000000003";
    private const string ReportedByUserId = "40000000-0000-0000-0000-000000000004";

    private readonly List<HttpRequestMessage> maintenanceRequests = [];
    private readonly List<string> maintenanceBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, Defect());

    [Fact]
    public void A_reported_defect_offers_triage_and_reject_with_the_manage_permission()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(".defect-triage-open"));
        Assert.NotNull(page.Find(".defect-reject-open"));
        Assert.Empty(page.FindAll(".defect-convert-open"));
    }

    [Fact]
    public void A_reported_defect_hides_every_action_without_the_manage_permission()
    {
        var page = RenderPage(permissions: "maintenance.read");

        Assert.Empty(page.FindAll(".defect-triage-open"));
        Assert.Empty(page.FindAll(".defect-reject-open"));
        Assert.Empty(page.FindAll(".defect-convert-open"));
        Assert.NotNull(page.Find(".work-order-details-readonly"));
    }

    [Fact]
    public void A_triaged_defect_offers_convert_and_reject_and_shows_the_triage_record()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Defect(
                status: "Triaged",
                confirmedSeverity: "\"Critical\"",
                priority: "\"High\"",
                triageNotes: "\"Confirmed on inspection\"",
                triagedByUserId: $"\"{ReportedByUserId}\"",
                triagedAtUtc: "\"2026-08-21T09:00:00Z\""));

        var page = RenderPage();

        Assert.NotNull(page.Find(".defect-convert-open"));
        Assert.NotNull(page.Find(".defect-reject-open"));
        Assert.Empty(page.FindAll(".defect-triage-open"));

        var record = page.Find("[aria-labelledby='defect-triage-record-title']").TextContent;

        Assert.Contains("Critical", record, StringComparison.Ordinal);
        Assert.Contains("High", record, StringComparison.Ordinal);
        Assert.Contains("Confirmed on inspection", record, StringComparison.Ordinal);
    }

    [Fact]
    public void A_converted_defect_shows_no_actions_and_a_working_work_order_link()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Defect(
                status: "Converted",
                confirmedSeverity: "\"High\"",
                priority: "\"High\"",
                triagedAtUtc: "\"2026-08-21T09:00:00Z\"",
                workOrderId: $"\"{WorkOrderId}\"",
                convertedAtUtc: "\"2026-08-22T09:00:00Z\""));

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-details-actions"));
        Assert.NotNull(page.Find(".work-order-details-closed"));

        var link = page.Find("[aria-labelledby='defect-conversion-title'] a");

        Assert.Equal($"/maintenance/work-orders/{WorkOrderId}", link.GetAttribute("href"));
    }

    [Fact]
    public void A_rejected_defect_shows_no_actions_and_the_rejection_reason()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            Defect(status: "Rejected", rejectionReason: "\"Duplicate of another report\""));

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-details-actions"));
        Assert.NotNull(page.Find(".work-order-details-closed"));
        Assert.Contains(
            "Duplicate of another report",
            page.Find("[aria-labelledby='defect-rejection-title']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_triage_modal_opens_on_click()
    {
        var page = RenderPage();

        Assert.Empty(page.FindAll(".maintenance-modal-backdrop"));

        page.Find(".defect-triage-open").Click();

        Assert.NotNull(page.Find(".maintenance-modal-backdrop .maintenance-form-card"));
    }

    [Fact]
    public void Cancelling_the_triage_modal_closes_it_without_submitting()
    {
        var page = RenderPage();

        page.Find(".defect-triage-open").Click();
        FindReset(page, "Cancel").Click();

        Assert.Empty(page.FindAll(".maintenance-modal-backdrop"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_successful_triage_updates_the_status_in_place_with_no_navigation()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.OK,
                Defect(
                    status: "Triaged",
                    confirmedSeverity: "\"High\"",
                    priority: "\"High\"",
                    triageNotes: "\"Looks urgent\"",
                    triagedAtUtc: "\"2026-08-21T09:00:00Z\""))
            : Json(HttpStatusCode.OK, Defect());

        var page = RenderPage();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var uriBefore = navigation.Uri;

        page.Find(".defect-triage-open").Click();
        page.Find("#defect-triage-severity").Change("High");
        page.Find("#defect-triage-priority").Change("High");
        page.Find("#defect-triage-notes").Change("Looks urgent");
        page.Find(".maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".maintenance-modal-backdrop"));
            Assert.Equal("Triaged", page.Find(".work-order-status").TextContent.Trim());
            Assert.Contains(
                "The defect was triaged.",
                page.Find(".work-order-details-success").TextContent,
                StringComparison.Ordinal);
        });

        Assert.Equal(uriBefore, navigation.Uri);
    }

    [Fact]
    public void A_triage_validation_failure_keeps_the_form_open()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"priority":["Priority is required."]}}""")
            : Json(HttpStatusCode.OK, Defect());

        var page = RenderPage();

        page.Find(".defect-triage-open").Click();
        page.Find("#defect-triage-severity").Change("High");
        page.Find("#defect-triage-priority").Change("High");
        page.Find(".maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".maintenance-modal-backdrop"));
            Assert.Contains(
                "Priority is required.",
                page.Find(".maintenance-field-errors").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_reject_confirm_button_is_disabled_and_nothing_is_sent_with_an_empty_reason()
    {
        var page = RenderPage();

        page.Find(".defect-reject-open").Click();

        Assert.True(page.Find(".documents-search-button").HasAttribute("disabled"));

        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Cancelling_the_reject_panel_closes_it_without_submitting()
    {
        var page = RenderPage();

        page.Find(".defect-reject-open").Click();
        page.Find("#defect-reject-reason").Change("Not a real defect");
        page.Find(".documents-modal-cancel").Click();

        Assert.Empty(page.FindAll(".documents-lifecycle-panel"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_successful_reject_updates_the_status_in_place()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, Defect(status: "Rejected", rejectionReason: "\"Not a real defect\""))
            : Json(HttpStatusCode.OK, Defect());

        var page = RenderPage();

        page.Find(".defect-reject-open").Click();
        page.Find("#defect-reject-reason").Change("Not a real defect");
        page.Find(".documents-search-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".documents-lifecycle-panel"));
            Assert.Equal("Rejected", page.Find(".work-order-status").TextContent.Trim());
            Assert.Contains(
                "The defect was rejected.",
                page.Find(".work-order-details-success").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "Not a real defect",
                page.Find("[aria-labelledby='defect-rejection-title']").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Cancelling_the_convert_modal_closes_it_without_submitting()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Defect(status: "Triaged", priority: "\"High\"", triagedAtUtc: "\"2026-08-21T09:00:00Z\""));

        var page = RenderPage();

        page.Find(".defect-convert-open").Click();
        FindReset(page, "Cancel").Click();

        Assert.Empty(page.FindAll(".maintenance-modal-backdrop"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_successful_convert_sends_the_due_date_and_shows_the_work_order_link_with_no_navigation()
    {
        var triagedDefect = Defect(status: "Triaged", priority: "\"High\"", triagedAtUtc: "\"2026-08-21T09:00:00Z\"");
        var convertedDefect = Defect(
            status: "Converted",
            priority: "\"High\"",
            triagedAtUtc: "\"2026-08-21T09:00:00Z\"",
            workOrderId: $"\"{WorkOrderId}\"",
            convertedAtUtc: "\"2026-08-23T09:00:00Z\"");
        var converted = false;

        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                converted = true;

                return Json(HttpStatusCode.Created, $$"""{"defectId":"{{DefectId}}","workOrderId":"{{WorkOrderId}}"}""");
            }

            return Json(HttpStatusCode.OK, converted ? convertedDefect : triagedDefect);
        };

        var page = RenderPage();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var uriBefore = navigation.Uri;

        page.Find(".defect-convert-open").Click();
        page.Find("#defect-convert-due").Change("2026-09-01T10:00:00");
        page.Find(".maintenance-form").Submit();

        Assert.Equal(uriBefore, navigation.Uri);

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                maintenanceBodies,
                body => body.Contains("\"dueUtc\":\"2026-09-01T10:00:00Z\"", StringComparison.Ordinal));

            Assert.Contains(
                "The defect was converted to a work order.",
                page.Find(".work-order-details-success").TextContent,
                StringComparison.Ordinal);

            var link = page.Find(".work-order-details-success a");

            Assert.Equal("View work order", link.TextContent.Trim());
            Assert.Equal($"/maintenance/work-orders/{WorkOrderId}", link.GetAttribute("href"));
        });

        Assert.Equal(uriBefore, navigation.Uri);
    }

    [Fact]
    public void A_concurrency_conflict_on_triage_reloads_automatically_and_closes_the_modal()
    {
        var conflicted = false;
        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                conflicted = true;

                return Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Defect was modified concurrently.","detail":"The defect was changed by another request. Reload and try again."}""");
            }

            return Json(HttpStatusCode.OK, Defect(status: conflicted ? "Triaged" : "Reported"));
        };

        var page = RenderPage();
        var getsBefore = DefectGetCount();

        page.Find(".defect-triage-open").Click();
        page.Find("#defect-triage-severity").Change("High");
        page.Find("#defect-triage-priority").Change("High");
        page.Find(".maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".maintenance-modal-backdrop"));
            Assert.True(DefectGetCount() > getsBefore);
        });
    }

    [Fact]
    public void A_business_conflict_on_triage_shows_a_banner_with_a_manual_refresh()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Defect status transition is not allowed.","detail":"This defect has already been converted."}""")
            : Json(HttpStatusCode.OK, Defect());

        var page = RenderPage();

        page.Find(".defect-triage-open").Click();
        page.Find("#defect-triage-severity").Change("High");
        page.Find("#defect-triage-priority").Change("High");
        page.Find(".maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".maintenance-modal-backdrop"));
            Assert.Contains(
                "This defect has already been converted.",
                page.Find(".maintenance-inline-error").TextContent,
                StringComparison.Ordinal);
        });

        var getsBefore = DefectGetCount();

        page.FindAll(".maintenance-modal-backdrop .maintenance-reset")
            .Single(button => button.TextContent.Trim() == "Refresh")
            .Click();

        page.WaitForAssertion(() => Assert.True(DefectGetCount() > getsBefore));
    }

    [Fact]
    public void A_concurrency_conflict_on_reject_reloads_automatically_and_closes_the_panel()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Defect was modified concurrently.","detail":"The defect was changed by another request. Reload and try again."}""")
            : Json(HttpStatusCode.OK, Defect());

        var page = RenderPage();
        var getsBefore = DefectGetCount();

        page.Find(".defect-reject-open").Click();
        page.Find("#defect-reject-reason").Change("Not a real defect");
        page.Find(".documents-search-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".documents-lifecycle-panel"));
            Assert.True(DefectGetCount() > getsBefore);
        });
    }

    [Fact]
    public void A_load_failure_shows_the_api_error_state_and_retry_reissues_the_load()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load the defect","detail":"The register is offline."}""");

        var page = RenderPage();

        Assert.Contains(
            "Unable to load the defect",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);

        var getsBefore = DefectGetCount();

        respondToMaintenance = _ => Json(HttpStatusCode.OK, Defect());

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(DefectGetCount() > getsBefore);
            Assert.NotEmpty(page.FindAll(".work-order-details-header h1"));
        });
    }

    private int DefectGetCount()
    {
        return maintenanceRequests.Count(request =>
            request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == $"/api/maintenance/defects/{DefectId}");
    }

    private static IElement FindReset(IRenderedComponent<DefectDetails> page, string text)
    {
        return page.FindAll(".maintenance-modal-backdrop .maintenance-reset")
            .Single(button => button.TextContent.Trim() == text);
    }

    private IRenderedComponent<DefectDetails> RenderPage(params string[] permissions)
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

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var page = Render<DefectDetails>(parameters => parameters
            .Add(component => component.DefectId, Guid.Parse(DefectId)));

        page.WaitForAssertion(() => Assert.NotEmpty(maintenanceRequests));

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

    private static string Defect(
        string status = "Reported",
        string reportedSeverity = "High",
        string confirmedSeverity = "null",
        string priority = "null",
        string triageNotes = "null",
        string triagedByUserId = "null",
        string triagedAtUtc = "null",
        string rejectionReason = "null",
        string rejectedByUserId = "null",
        string rejectedAtUtc = "null",
        string workOrderId = "null",
        string convertedByUserId = "null",
        string convertedAtUtc = "null")
    {
        return $$"""
            {"id":"{{DefectId}}","vesselId":"{{VesselId}}","equipmentId":null,
            "title":"Cracked bracket","description":"Bracket cracked during last inspection",
            "reportedSeverity":"{{reportedSeverity}}","observedAtUtc":"2026-08-19T08:00:00Z",
            "status":"{{status}}","reportedByUserId":"{{ReportedByUserId}}",
            "reportedAtUtc":"2026-08-20T10:00:00Z","confirmedSeverity":{{confirmedSeverity}},
            "priority":{{priority}},"triageNotes":{{triageNotes}},"triagedByUserId":{{triagedByUserId}},
            "triagedAtUtc":{{triagedAtUtc}},"rejectionReason":{{rejectionReason}},
            "rejectedByUserId":{{rejectedByUserId}},"rejectedAtUtc":{{rejectedAtUtc}},
            "workOrderId":{{workOrderId}},"convertedByUserId":{{convertedByUserId}},
            "convertedAtUtc":{{convertedAtUtc}}}
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
