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

public sealed class MaintenanceSchedulesPanelTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";
    private const string EquipmentId = "c0000000-0000-0000-0000-00000000000c";
    private const string ScheduleId = "d0000000-0000-0000-0000-00000000000d";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondToMaintenanceWithToken;

    [Fact]
    public void Schedules_are_listed_with_the_looked_up_vessel_name()
    {
        var panel = RenderPanel();

        var row = panel.Find(".schedules-row");

        Assert.Contains("Quarterly inspection", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Quarterly", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("Aurora Borealis", panel.Find(".schedules-vessel").TextContent);
    }

    [Fact]
    public void Overdue_schedules_are_marked_with_text_not_colour_alone()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            SchedulesPage(Schedule(isOverdue: "true", daysUntilDue: "-4")));

        var panel = RenderPanel();

        Assert.Single(panel.FindAll(".schedules-row.maintenance-overdue-row"));
        Assert.Equal("Overdue", panel.Find(".schedules-due-badge").TextContent.Trim());
    }

    [Fact]
    public void Upcoming_schedules_show_the_days_reported_by_the_api()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            SchedulesPage(Schedule(isOverdue: "false", daysUntilDue: "12")));

        var panel = RenderPanel();

        Assert.Equal("Due in 12 days", panel.Find(".schedules-due-badge").TextContent.Trim());
    }

    [Fact]
    public void Running_hours_schedules_show_a_placeholder_date_and_an_hours_badge()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.OK,
            SchedulesPage(Schedule(
                frequency: "RunningHours",
                nextDueDate: "null",
                nextDueHours: "800",
                isOverdue: "false",
                daysUntilDue: "null")));

        var panel = RenderPanel();

        var cells = panel.Find(".schedules-row").QuerySelectorAll("td");

        Assert.Equal(MaintenanceFormat.MissingValue, cells[2].TextContent);
        Assert.Equal("800", cells[3].TextContent);
        Assert.Equal("Running hours only", panel.Find(".schedules-due-badge").TextContent.Trim());
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#schedules-due-from").Change("2026-09-01");
        panel.Find("#schedules-due-to").Change("2026-10-01");
        panel.Find("#schedules-overdue").Change("true");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == "/api/maintenance/schedules").Query;

            Assert.Contains("dueFrom=2026-09-01", query, StringComparison.Ordinal);
            Assert.Contains("dueTo=2026-10-01", query, StringComparison.Ordinal);
            Assert.Contains("overdue=true", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Creating_a_schedule_posts_the_selected_frequency_and_equipment()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{ScheduleId}}"}""")
            : request.RequestUri!.AbsolutePath == "/api/maintenance/equipment"
                ? Json(HttpStatusCode.OK, EquipmentPage())
                : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-register-toggle").Click();

        SelectVesselInTheRegisterForm(panel);

        panel.Find("#schedule-type").Change("Quarterly inspection");
        panel.Find("#schedule-frequency").Change("Quarterly");
        panel.Find("#schedule-next-due-date").Change("2026-09-30");
        panel.Find(".schedules-equipment-option input").Change(true);
        panel.Find(".schedules-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Preventive schedule was registered successfully.",
                panel.Find(".schedules-success").TextContent,
                StringComparison.Ordinal);

            var body = requestBodies.Last();

            Assert.Contains("\"frequency\":\"Quarterly\"", body, StringComparison.Ordinal);
            Assert.Contains("\"nextDueDate\":\"2026-09-30\"", body, StringComparison.Ordinal);
            Assert.Contains(EquipmentId, body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Creating_a_schedule_shows_the_validation_errors_returned_by_the_api()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.",
                "errors":{"equipmentIds":["At least one equipment ID is required."]}}
                """)
            : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-register-toggle").Click();
        panel.Find(".schedules-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".schedules-field-errors");

            Assert.Contains("Please correct the following before saving", errors.TextContent, StringComparison.Ordinal);
            Assert.Contains("At least one equipment ID is required.", errors.TextContent, StringComparison.Ordinal);
            Assert.Equal("Equipment", panel.Find(".schedules-field-errors .maintenance-error-field").TextContent);
        });
    }

    [Fact]
    public void Required_fields_are_marked_in_the_register_form()
    {
        var panel = RenderPanel();

        panel.Find(".schedules-register-toggle").Click();

        var markedLabels = panel.FindAll(".schedules-form-card .required-marker")
            .Select(marker => marker.ParentElement!.TextContent.Trim())
            .ToList();

        Assert.Equal(4, markedLabels.Count);
        Assert.Contains(markedLabels, label => label.StartsWith("Vessel", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Maintenance type", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Frequency", StringComparison.Ordinal));
        Assert.Contains(markedLabels, label => label.StartsWith("Equipment", StringComparison.Ordinal));
    }

    [Fact]
    public void The_success_toast_disappears_on_its_own()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, $$"""{"id":"{{ScheduleId}}"}""")
            : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-register-toggle").Click();
        panel.Find(".schedules-form").Submit();

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".maintenance-toast")));

        panel.WaitForAssertion(
            () => Assert.Empty(panel.FindAll(".maintenance-toast")),
            TimeSpan.FromMilliseconds(PreventiveSchedulesPanel.SuccessToastMilliseconds + 3000));
    }

    [Fact]
    public void Creating_a_schedule_shows_a_vessel_conflict_as_an_inline_error()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"Maintenance schedule equipment spans more than one vessel.",
                "detail":"A maintenance schedule must cover equipment of a single vessel."}
                """)
            : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-register-toggle").Click();
        panel.Find(".schedules-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "single vessel",
            panel.Find(".schedules-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Details_show_the_linked_equipment()
    {
        respondToMaintenance = request =>
            request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                ? Json(HttpStatusCode.OK, ScheduleDetails())
                : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-details").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "Main engine",
            panel.Find(".schedules-equipment-row").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Details_show_a_not_found_schedule_as_an_inline_error()
    {
        respondToMaintenance = request =>
            request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                ? Json(
                    HttpStatusCode.NotFound,
                    """{"title":"Maintenance schedule not found.","detail":"It may have been removed."}""")
                : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-details").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "It may have been removed.",
            panel.Find(".schedules-details-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Recording_a_completion_posts_only_the_completion_date()
    {
        respondToMaintenance = request =>
            request.Method == HttpMethod.Post
                ? Json(
                    HttpStatusCode.OK,
                    $$"""
                    {"scheduleId":"{{ScheduleId}}","frequency":"Quarterly",
                    "nextDueDate":"2026-11-18","nextDueHours":null}
                    """)
                : request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                    ? Json(HttpStatusCode.OK, ScheduleDetails())
                    : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-details").Click();
        panel.WaitForElement(".schedules-completion-form");

        panel.Find("#completion-date").Change("2026-08-18");
        panel.Find(".schedules-completion-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Completion was recorded and the next due values were updated.",
                panel.Find(".schedules-completion-success").TextContent,
                StringComparison.Ordinal);

            Assert.Contains(
                requestBodies,
                body => body.Contains("\"completedOn\":\"2026-08-18\"", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Recording_a_completion_shows_the_running_hours_validation_error()
    {
        respondToMaintenance = request =>
            request.Method == HttpMethod.Post
                ? Json(
                    HttpStatusCode.BadRequest,
                    """
                    {"title":"One or more validation errors occurred.",
                    "errors":{"nextDueHours":["Next due hours is required for a running-hours schedule."]}}
                    """)
                : request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                    ? Json(HttpStatusCode.OK, ScheduleDetails())
                    : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-details").Click();
        panel.WaitForElement(".schedules-completion-form");

        panel.Find("#completion-date").Change("2026-08-18");
        panel.Find(".schedules-completion-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "running-hours schedule",
            panel.Find(".schedules-completion-errors").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_completion_form_is_hidden_without_the_manage_permission()
    {
        respondToMaintenance = request =>
            request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                ? Json(HttpStatusCode.OK, ScheduleDetails())
                : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel("maintenance.read");

        panel.Find(".schedules-details").Click();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".schedules-details-summary")));

        Assert.Empty(panel.FindAll(".schedules-register-toggle"));
        Assert.Empty(panel.FindAll(".schedules-completion-form"));
    }

    [Fact]
    public void The_register_form_opens_as_a_modal_dialog()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));

        panel.Find(".schedules-register-toggle").Click();

        var dialog = panel.Find(".maintenance-modal-backdrop .schedules-form-card");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
    }

    [Fact]
    public void The_details_view_opens_as_a_modal_dialog_and_closes_from_the_backdrop()
    {
        respondToMaintenance = request =>
            request.RequestUri!.AbsolutePath == $"/api/maintenance/schedules/{ScheduleId}"
                ? Json(HttpStatusCode.OK, ScheduleDetails())
                : Json(HttpStatusCode.OK, SchedulesPage(Schedule()));

        var panel = RenderPanel();

        panel.Find(".schedules-details").Click();

        panel.WaitForAssertion(() =>
        {
            var dialog = panel.Find(".maintenance-modal-backdrop .schedules-details-card");

            Assert.Equal("dialog", dialog.GetAttribute("role"));
            Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        });

        panel.Find(".maintenance-modal-backdrop").Click();

        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"Maintenance access denied","detail":"Missing permission."}""");

        var panel = RenderPanel();

        Assert.Contains(
            "Maintenance access denied",
            panel.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_result_shows_the_empty_state()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, SchedulesPage());

        var panel = RenderPanel();

        Assert.Contains("No preventive schedules", panel.Find(".empty-state").TextContent, StringComparison.Ordinal);
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

    private static void SelectVesselInTheRegisterForm(IRenderedComponent<PreventiveSchedulesPanel> panel)
    {
        panel.Find(".schedules-form-card .vessel-picker-input").Input("Aurora");
        panel.WaitForElement(".schedules-form-card .vessel-picker-option", TimeSpan.FromSeconds(5));
        panel.Find(".schedules-form-card .vessel-picker-option").Click();
        panel.WaitForElement(".schedules-equipment-option", TimeSpan.FromSeconds(5));
    }

    private IRenderedComponent<PreventiveSchedulesPanel> RenderPanel(params string[] permissions)
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
            (_, _) => Json(HttpStatusCode.OK, VesselPage())))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new VesselLookupApiClient(lookupHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var panel = Render<PreventiveSchedulesPanel>();
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

    private static string Schedule(
        string maintenanceType = "Quarterly inspection",
        string frequency = "Quarterly",
        string nextDueDate = "\"2026-09-30\"",
        string nextDueHours = "null",
        string isOverdue = "false",
        string daysUntilDue = "12",
        int equipmentCount = 2)
    {
        return $$"""
            {"scheduleId":"{{ScheduleId}}","vesselId":"{{VesselId}}","maintenanceType":"{{maintenanceType}}",
            "frequency":"{{frequency}}","nextDueDate":{{nextDueDate}},"nextDueHours":{{nextDueHours}},
            "isOverdue":{{isOverdue}},"daysUntilDue":{{daysUntilDue}},"equipmentCount":{{equipmentCount}}}
            """;
    }

    private static string SchedulesPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":25,"totalCount":{{items.Length}}}
            """;
    }

    private static string ScheduleDetails()
    {
        return $$"""
            {"scheduleId":"{{ScheduleId}}","vesselId":"{{VesselId}}","maintenanceType":"Quarterly inspection",
            "frequency":"Quarterly","nextDueDate":"2026-09-30","nextDueHours":null,
            "isOverdue":false,"daysUntilDue":12,
            "equipment":[{"equipmentId":"{{EquipmentId}}","vesselId":"{{VesselId}}","name":"Main engine",
            "category":"Propulsion","manufacturer":null,"serialNumber":null}]}
            """;
    }

    private static string EquipmentPage()
    {
        return $$"""
            {"items":[{"equipmentId":"{{EquipmentId}}","vesselId":"{{VesselId}}","name":"Main engine",
            "category":"Propulsion","manufacturer":null,"serialNumber":null}],
            "pageNumber":1,"pageSize":100,"totalCount":1}
            """;
    }

    private static string VesselPage()
    {
        return $$"""
            {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"IMO9000001",
            "flagState":"NO","currentStatus":"Active","vesselTypeId":"40000000-0000-0000-0000-000000000004",
            "vesselTypeName":"Tanker","companyId":"50000000-0000-0000-0000-000000000005","dwt":null,
            "updatedAtUtc":"2026-08-01T08:00:00Z","lifecycleStage":null,"fleetId":null,"fleetName":null}],
            "pageNumber":1,"pageSize":10,"totalCount":1}
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
