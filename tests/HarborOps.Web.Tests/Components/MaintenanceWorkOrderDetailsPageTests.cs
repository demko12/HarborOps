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

public sealed class MaintenanceWorkOrderDetailsPageTests : BunitContext
{
    private const string WorkOrderId = "10000000-0000-0000-0000-000000000001";
    private const string VesselId = "20000000-0000-0000-0000-000000000002";
    private const string MainEngineId = "30000000-0000-0000-0000-000000000003";
    private const string BoilerId = "40000000-0000-0000-0000-000000000004";
    private const string PartId = "50000000-0000-0000-0000-000000000005";

    private readonly List<HttpRequestMessage> maintenanceRequests = [];
    private readonly List<string> maintenanceBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance = Default;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_details_are_requested_for_the_work_order_in_the_route()
    {
        RenderPage();

        Assert.Contains(
            maintenanceRequests,
            request => request.RequestUri!.AbsolutePath == $"/api/maintenance/work-orders/{WorkOrderId}");
    }

    [Fact]
    public void The_scope_is_shown_with_the_looked_up_vessel_name()
    {
        var page = RenderPage();

        Assert.Equal("Replace main engine filter", page.Find(".work-order-details-header h1").TextContent.Trim());
        Assert.Equal("Aurora Borealis", page.Find(".work-order-details-vessel a").TextContent.Trim());
        Assert.Equal("Open", page.Find(".work-order-status").TextContent.Trim());
        Assert.Equal("High", page.Find(".work-order-priority").TextContent.Trim());
        Assert.Contains(
            "Filter is due for replacement",
            page.Find(".work-order-details-description").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_work_order_without_scope_shows_no_equipment_no_usage_and_no_completion()
    {
        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-equipment-list"));
        Assert.Empty(page.FindAll(".work-order-table"));
        Assert.DoesNotContain("Completion record", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_linked_equipment_is_listed()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(HttpStatusCode.OK, Details(equipment: $"[{Equipment()},{Equipment(BoilerId, "Boiler", "Steam")}]"));

        var page = RenderPage();

        var names = page.FindAll(".work-order-equipment-list .work-order-equipment-name");

        Assert.Equal(2, names.Count);
        Assert.Equal("Main engine", names[0].TextContent.Trim());
        Assert.Equal("Boiler", names[1].TextContent.Trim());
    }

    [Fact]
    public void The_recorded_part_usage_is_listed_with_the_remaining_quantity()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage())
            : Json(HttpStatusCode.OK, Details(partUsages: $"[{Usage()}]"));

        var page = RenderPage();
        var row = page.Find(".work-order-table tbody tr");

        Assert.Contains("Main engine filter", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("FILTER-001", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("2.50", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("7.50", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_completed_work_order_shows_its_completion_record()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage())
            : Json(
                HttpStatusCode.OK,
                Details(
                    status: "Completed",
                    allowedActions: "[]",
                    completedUtc: "\"2026-08-20T14:30:00Z\"",
                    completion: Completion()));

        var page = RenderPage();

        Assert.Contains("Completion record", page.Markup, StringComparison.Ordinal);
        Assert.Contains("2026-08-20", page.Markup, StringComparison.Ordinal);
        Assert.Contains("1,250.50", page.Markup, StringComparison.Ordinal);
        Assert.Contains(
            "Filter replaced and tested",
            page.Find(".work-order-details-notes").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_overdue_work_order_is_flagged_in_the_header()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage())
            : Json(HttpStatusCode.OK, Details(isOverdue: "true"));

        var page = RenderPage();

        Assert.NotNull(page.Find(".work-order-overdue-badge"));
    }

    [Fact]
    public void An_open_work_order_offers_starting_cancelling_and_scoping()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(".work-order-start"));
        Assert.NotNull(page.Find(".work-order-cancel"));
        Assert.NotNull(page.Find(".work-order-equipment-open"));
    }

    [Fact]
    public void A_work_order_in_progress_can_no_longer_be_started()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage())
            : Json(
                HttpStatusCode.OK,
                Details(status: "InProgress", allowedActions: """["Cancel","ReplaceEquipment"]"""));

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-start"));
        Assert.NotNull(page.Find(".work-order-cancel"));
        Assert.NotNull(page.Find(".work-order-equipment-open"));
    }

    [Fact]
    public void A_closed_work_order_offers_no_action_at_all()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage())
            : Json(HttpStatusCode.OK, Details(status: "Canceled", allowedActions: "[]"));

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-details-actions"));
        Assert.Empty(page.FindAll(".work-order-equipment-open"));
        Assert.NotNull(page.Find(".work-order-details-closed"));
    }

    [Fact]
    public void A_reader_without_the_manage_permission_is_offered_no_action()
    {
        var page = RenderPage(boardQuery: null, "maintenance.read");

        Assert.Empty(page.FindAll(".work-order-details-actions"));
        Assert.Empty(page.FindAll(".work-order-equipment-open"));
        Assert.NotNull(page.Find(".work-order-details-readonly"));
    }

    [Fact]
    public void Starting_asks_for_confirmation_before_calling_the_api()
    {
        var page = RenderPage();

        page.Find(".work-order-start").Click();

        Assert.NotNull(page.Find(".work-order-confirm-card"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Dismissing_the_confirmation_leaves_the_work_order_untouched()
    {
        var page = RenderPage();

        page.Find(".work-order-start").Click();
        page.Find(".work-order-confirm-dismiss").Click();

        Assert.Empty(page.FindAll(".work-order-confirm-card"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_confirmed_start_posts_to_the_start_route_and_refreshes_the_details()
    {
        var page = RenderPage();
        var detailsReadsBefore = DetailsReadCount();

        page.Find(".work-order-start").Click();
        page.Find(".work-order-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                maintenanceRequests,
                request => request.Method == HttpMethod.Post
                    && request.RequestUri!.AbsolutePath.EndsWith("/start", StringComparison.Ordinal));
            Assert.Empty(page.FindAll(".work-order-confirm-card"));
            Assert.True(DetailsReadCount() > detailsReadsBefore);
            Assert.Contains(
                "started",
                page.Find(".work-order-details-success").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_confirmed_cancel_posts_to_the_cancel_route()
    {
        var page = RenderPage();

        page.Find(".work-order-cancel").Click();
        page.Find(".work-order-confirm").Click();

        page.WaitForAssertion(() => Assert.Contains(
            maintenanceRequests,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_conflict_reports_the_reason_and_reloads_the_authoritative_status()
    {
        var posted = false;
        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted = true;

                return Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Maintenance work order status transition is not allowed.","detail":"Already started."}""");
            }

            if (IsEquipmentRequest(request))
            {
                return Json(HttpStatusCode.OK, EquipmentPage());
            }

            return Json(
                HttpStatusCode.OK,
                posted
                    ? Details(status: "InProgress", allowedActions: """["Cancel","ReplaceEquipment"]""")
                    : Details());
        };

        var page = RenderPage();

        page.Find(".work-order-start").Click();
        page.Find(".work-order-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".work-order-confirm-card"));
            Assert.Contains(
                "Already started.",
                page.Find(".work-order-details-error").TextContent,
                StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".work-order-start"));
        });
    }

    [Fact]
    public void Opening_the_picker_loads_the_equipment_of_this_vessel_only()
    {
        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();

        page.WaitForAssertion(() =>
        {
            var request = Assert.Single(maintenanceRequests, IsEquipmentRequest);

            Assert.Contains($"vesselId={VesselId}", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains(
                $"pageSize={MaintenanceWorkOrderDetails.EquipmentPickerPageSize}",
                request.RequestUri.Query,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_picker_preselects_the_equipment_already_linked()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment(), Equipment(BoilerId, "Boiler", "Steam")))
            : Json(HttpStatusCode.OK, Details(equipment: $"[{Equipment()}]"));

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();

        page.WaitForAssertion(() =>
        {
            var boxes = page.FindAll(".work-order-equipment-picker input");

            Assert.Equal(2, boxes.Count);
            Assert.True(boxes[0].HasAttribute("checked"));
            Assert.False(boxes[1].HasAttribute("checked"));
            Assert.Contains("1 selected", page.Find(".work-order-picker-count").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Saving_the_picker_replaces_the_scope_through_the_api()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Put
            ? Json(HttpStatusCode.OK, ReplaceResult($"[{Equipment()}]"))
            : IsEquipmentRequest(request)
                ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
                : Json(HttpStatusCode.OK, Details());

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-equipment-picker input")));

        page.Find(".work-order-equipment-picker input").Change(true);
        page.Find(".work-order-equipment-save").Click();

        page.WaitForAssertion(() =>
        {
            var request = Assert.Single(maintenanceRequests, item => item.Method == HttpMethod.Put);

            Assert.Equal(
                $"/api/maintenance/work-orders/{WorkOrderId}/equipment",
                request.RequestUri!.AbsolutePath);
            Assert.Contains(MainEngineId, Assert.Single(maintenanceBodies), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(page.FindAll(".work-order-equipment-picker"));
            Assert.NotNull(page.Find(".work-order-details-success"));
        });
    }

    [Fact]
    public void Clearing_every_selection_sends_an_empty_scope()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Put
            ? Json(HttpStatusCode.OK, ReplaceResult("[]"))
            : IsEquipmentRequest(request)
                ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
                : Json(HttpStatusCode.OK, Details(equipment: $"[{Equipment()}]"));

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-equipment-picker input")));

        page.Find(".work-order-equipment-picker input").Change(false);
        page.Find(".work-order-equipment-save").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "\"equipmentIds\":[]",
            Assert.Single(maintenanceBodies).Replace(" ", string.Empty, StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Closing_the_picker_sends_nothing()
    {
        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-equipment-picker input")));

        page.Find(".work-order-equipment-dismiss").Click();

        Assert.Empty(page.FindAll(".work-order-equipment-picker"));
        Assert.DoesNotContain(maintenanceRequests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public void A_rejected_scope_reports_the_field_error_from_the_api()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"equipmentIds":["Must not contain duplicates."]}}""")
            : IsEquipmentRequest(request)
                ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
                : Json(HttpStatusCode.OK, Details());

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-equipment-picker input")));

        page.Find(".work-order-equipment-picker input").Change(true);
        page.Find(".work-order-equipment-save").Click();

        page.WaitForAssertion(() =>
        {
            var errors = page.Find(".work-order-field-errors").TextContent;

            Assert.Contains("Equipment", errors, StringComparison.Ordinal);
            Assert.Contains("Must not contain duplicates.", errors, StringComparison.Ordinal);
            Assert.NotNull(page.Find(".work-order-equipment-picker"));
        });
    }

    [Fact]
    public void A_scope_rejected_as_a_conflict_closes_the_picker_and_reloads()
    {
        var putSent = false;
        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                putSent = true;

                return Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Equipment does not belong to the work order vessel.","detail":"Other vessel."}""");
            }

            if (IsEquipmentRequest(request))
            {
                return Json(HttpStatusCode.OK, EquipmentPage(Equipment()));
            }

            return Json(HttpStatusCode.OK, putSent ? Details(status: "Canceled", allowedActions: "[]") : Details());
        };

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-equipment-picker input")));

        page.Find(".work-order-equipment-picker input").Change(true);
        page.Find(".work-order-equipment-save").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".work-order-equipment-picker"));
            Assert.Contains(
                "Other vessel.",
                page.Find(".work-order-details-error").TextContent,
                StringComparison.Ordinal);
            Assert.NotNull(page.Find(".work-order-details-closed"));
        });
    }

    [Fact]
    public void A_failed_equipment_load_offers_a_retry_inside_the_picker()
    {
        respondToMaintenance = request => IsEquipmentRequest(request)
            ? Json(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred."}""")
            : Json(HttpStatusCode.OK, Details());

        var page = RenderPage();

        page.Find(".work-order-equipment-open").Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".api-error-state"));
            Assert.True(page.Find(".work-order-equipment-save").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void An_unknown_work_order_reports_that_it_is_gone_without_offering_a_retry()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.NotFound,
            """{"title":"Maintenance work order not found.","detail":"Gone."}""");

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find(".empty-state"));
            Assert.Empty(page.FindAll(".api-error-state"));
        });
    }

    [Fact]
    public void A_forbidden_read_explains_the_missing_permission()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".empty-state")));
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respondToMaintenance = _ => throw new HttpRequestException("boom");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".api-error-state")));
    }

    [Fact]
    public async Task Disposing_the_page_cancels_the_request_in_flight()
    {
        var started = new TaskCompletionSource();
        var observed = CancellationToken.None;

        respondWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderPage();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void The_breadcrumb_links_back_to_the_maintenance_workspace()
    {
        var page = RenderPage();

        Assert.Equal("/maintenance", page.Find(".page-breadcrumb a").GetAttribute("href"));
    }

    [Fact]
    public void The_breadcrumb_returns_to_the_board_the_engineer_left()
    {
        var page = RenderPage(boardQuery: "tab=work-orders&status=Open");

        Assert.Equal(
            "/maintenance?tab=work-orders&status=Open",
            page.Find(".page-breadcrumb a").GetAttribute("href"));
    }

    private int DetailsReadCount()
    {
        return maintenanceRequests.Count(request =>
            request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == $"/api/maintenance/work-orders/{WorkOrderId}");
    }

    private static bool IsEquipmentRequest(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath == "/api/maintenance/equipment";
    }

    private static Task<HttpResponseMessage> Default(HttpRequestMessage request)
    {
        return IsEquipmentRequest(request)
            ? Json(HttpStatusCode.OK, EquipmentPage(Equipment()))
            : Json(HttpStatusCode.OK, Details());
    }

    private IRenderedComponent<MaintenanceWorkOrderDetails> RenderPage(
        string? boardQuery = null,
        params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            maintenanceRequests.Add(request);

            if (request.Content is not null)
            {
                maintenanceBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return respondWithToken is null
                ? await respondToMaintenance(request)
                : await respondWithToken(request, token);
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

        // A query-string parameter can only reach the component through the address bar.
        Services.GetRequiredService<NavigationManager>().NavigateTo(boardQuery is null
            ? $"/maintenance/work-orders/{WorkOrderId}"
            : $"/maintenance/work-orders/{WorkOrderId}?from={Uri.EscapeDataString(boardQuery)}");

        var page = Render<MaintenanceWorkOrderDetails>(parameters => parameters
            .Add(component => component.WorkOrderId, Guid.Parse(WorkOrderId)));

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

    private static string Details(
        string status = "Open",
        string priority = "High",
        string allowedActions = """["Start","Cancel","ReplaceEquipment"]""",
        string equipment = "[]",
        string partUsages = "[]",
        string completion = "null",
        string completedUtc = "null",
        string isOverdue = "false")
    {
        return $$"""
            {"workOrderId":"{{WorkOrderId}}","vesselId":"{{VesselId}}","dryDockId":null,
            "assignedEngineerId":null,"title":"Replace main engine filter",
            "description":"Filter is due for replacement","priority":"{{priority}}","status":"{{status}}",
            "dueUtc":"2026-12-01T10:00:00Z","createdUtc":"2026-08-01T08:00:00Z",
            "completedUtc":{{completedUtc}},"isOverdue":{{isOverdue}},"allowedActions":{{allowedActions}},
            "equipment":{{equipment}},"partUsages":{{partUsages}},"completion":{{completion}}}
            """;
    }

    private static string Equipment(
        string equipmentId = MainEngineId,
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
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,
            "pageSize":{{MaintenanceWorkOrderDetails.EquipmentPickerPageSize}},"totalCount":{{items.Length}}}
            """;
    }

    private static string Usage()
    {
        return $$"""
            {"partId":"{{PartId}}","name":"Main engine filter","partNumber":"FILTER-001",
            "quantityUsed":2.5,"remainingQuantity":7.5}
            """;
    }

    private static string Completion()
    {
        return """
            {"maintenanceRecordId":"60000000-0000-0000-0000-000000000006",
            "technicianId":"70000000-0000-0000-0000-000000000007","completionDate":"2026-08-20",
            "notes":"Filter replaced and tested","cost":1250.50,"laborHours":6.5}
            """;
    }

    private static string ReplaceResult(string equipment)
    {
        return $$"""
            {"workOrderId":"{{WorkOrderId}}","status":"Open",
            "allowedActions":["Start","Cancel","ReplaceEquipment"],"equipment":{{equipment}},
            "updatedUtc":"2026-08-02T09:00:00Z"}
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
