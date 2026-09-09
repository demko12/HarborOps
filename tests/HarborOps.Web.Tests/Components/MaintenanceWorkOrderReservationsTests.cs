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

public sealed class MaintenanceWorkOrderReservationsTests : BunitContext
{
    private const string WorkOrderId = "10000000-0000-0000-0000-000000000001";
    private const string VesselId = "20000000-0000-0000-0000-000000000002";
    private const string FilterId = "50000000-0000-0000-0000-000000000005";
    private const string GasketId = "60000000-0000-0000-0000-000000000006";

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> bodies = [];

    private string workOrderStatus = "Open";
    private string allowedActions = """["Start","Cancel","ReplaceEquipment"]""";
    private string reservationItems = $"[{Reservation()}]";
    private HttpStatusCode reservationsStatus = HttpStatusCode.OK;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? respondToReplace;
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? respondToRelease;
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_reservations_are_requested_for_the_work_order()
    {
        RenderPage();

        Assert.Contains(
            requests,
            request => request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath
                    == $"/api/maintenance/work-orders/{WorkOrderId}/part-reservations");
    }

    [Fact]
    public void A_reserved_part_is_listed_with_every_quantity()
    {
        var page = RenderPage();

        var row = page.Find(".work-order-reservations-table tbody tr");
        var cells = row.QuerySelectorAll("td");

        Assert.Equal("Main engine filter", cells[0].TextContent.Trim());
        Assert.Equal("FILTER-001", cells[1].TextContent.Trim());
        Assert.Equal("6", cells[2].TextContent.Trim());
        Assert.Equal("6", cells[3].TextContent.Trim());
        Assert.Equal("2", cells[4].TextContent.Trim());
        Assert.Equal("4", cells[5].TextContent.Trim());
        Assert.Equal("4", cells[6].TextContent.Trim());
        Assert.Equal("Reserved", cells[7].TextContent.Trim());
    }

    [Fact]
    public void A_work_order_without_reservations_says_so()
    {
        reservationItems = "[]";

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-reservations-table"));
        Assert.Contains(
            "No parts are reserved for this work order",
            page.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_shortage_is_flagged_on_the_row_and_in_a_banner()
    {
        reservationItems = $"[{Reservation(quantityRequested: "9", quantityReserved: "4", quantityConsumed: "0", quantityRemaining: "4", shortageQuantity: "5", hasShortage: "true")}]";

        var page = RenderPage();

        var badge = page.Find(".work-order-reservation-state");

        Assert.Contains("short", badge.ClassName, StringComparison.Ordinal);
        Assert.Equal("Short by 5", badge.TextContent.Trim());

        var banner = page.Find(".work-order-details-warning");
        var text = string.Join(' ', banner.TextContent.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("1 of 1 reserved parts are short by 5 in total", text, StringComparison.Ordinal);
        Assert.Equal("/maintenance?tab=replenishment", banner.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void A_failed_reservation_load_offers_a_retry_inside_the_card()
    {
        reservationsStatus = HttpStatusCode.InternalServerError;

        var page = RenderPage();
        var before = ReservationReadCount();

        reservationsStatus = HttpStatusCode.OK;
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(ReservationReadCount() > before);
            Assert.NotNull(page.Find(".work-order-reservations-table"));
        });
    }

    [Fact]
    public void A_reader_without_the_manage_permission_is_offered_no_reservation_action()
    {
        var page = RenderPage(permissions: "maintenance.read");

        Assert.Empty(page.FindAll(".work-order-reservations-edit"));
        Assert.Empty(page.FindAll(".work-order-reservations-release"));
    }

    [Fact]
    public void An_open_work_order_offers_editing_and_releasing()
    {
        var page = RenderPage();

        Assert.NotNull(page.Find(".work-order-reservations-edit"));
        Assert.NotNull(page.Find(".work-order-reservations-release"));
    }

    [Fact]
    public void A_work_order_in_progress_can_no_longer_change_its_reservations()
    {
        workOrderStatus = "InProgress";
        allowedActions = """["Cancel","ReplaceEquipment"]""";

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-reservations-edit"));
        Assert.Empty(page.FindAll(".work-order-reservations-release"));
    }

    [Fact]
    public void A_canceled_work_order_can_still_release_its_reservations()
    {
        workOrderStatus = "Canceled";
        allowedActions = "[]";

        var page = RenderPage();

        Assert.Empty(page.FindAll(".work-order-reservations-edit"));
        Assert.NotNull(page.Find(".work-order-reservations-release"));
    }

    [Fact]
    public void Opening_the_editor_preloads_the_current_reservations()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();

        page.WaitForAssertion(() =>
        {
            var rows = page.FindAll(".work-order-reservation-rows > li");

            Assert.Single(rows);
            Assert.Equal(FilterId, rows[0].QuerySelector("select")!.GetAttribute("value"));
            Assert.Equal("6", rows[0].QuerySelector("input")!.GetAttribute("value"));
        });
    }

    [Fact]
    public void Opening_the_editor_searches_the_inventory()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath == "/api/maintenance/inventory-items"));
    }

    [Fact]
    public void Searching_the_inventory_sends_the_term()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("#work-order-reservation-search-input")));

        page.Find("#work-order-reservation-search-input").Input("Gasket");

        page.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath == "/api/maintenance/inventory-items"
                && request.RequestUri.Query.Contains("search=Gasket", StringComparison.Ordinal)));
    }

    [Fact]
    public void Saving_the_editor_replaces_the_reservations_through_the_api()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservation-quantity").Change("9");
        page.Find(".work-order-reservations-save").Click();

        page.WaitForAssertion(() =>
        {
            var put = requests.Single(request =>
                request.Method == HttpMethod.Put
                && request.RequestUri!.AbsolutePath
                    == $"/api/maintenance/work-orders/{WorkOrderId}/part-reservations");

            Assert.NotNull(put);
            Assert.Contains(FilterId, bodies[^1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("9", bodies[^1], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Removing_every_row_sends_an_empty_reservation_list()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservation-remove").Click();
        page.Find(".work-order-reservations-save").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "\"reservations\":[]",
            bodies[^1].Replace(" ", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicated_part_is_flagged_and_blocks_saving()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservation-add").Click();

        var selects = page.FindAll(".work-order-reservation-part");
        selects[1].Change(FilterId);
        page.FindAll(".work-order-reservation-quantity")[1].Change("2");

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".work-order-reservation-duplicate"));
            Assert.True(page.Find(".work-order-reservations-save").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Closing_the_editor_sends_nothing()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservations-dismiss").Click();

        Assert.Empty(page.FindAll(".work-order-reservation-rows"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public void A_rejected_reservation_reports_the_field_error_from_the_api()
    {
        respondToReplace = _ => Json(
            HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.","status":400,
            "errors":{"reservations":["Must not contain duplicate inventoryItemId values."]}}
            """);

        var page = RenderPage();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservations-save").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Must not contain duplicate",
                page.Find(".work-order-field-errors").TextContent,
                StringComparison.Ordinal);
            Assert.NotEmpty(page.FindAll(".work-order-reservation-rows"));
        });
    }

    [Fact]
    public void A_conflict_closes_the_editor_and_reloads_the_authoritative_state()
    {
        respondToReplace = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"The work order is no longer open."}""");

        var page = RenderPage();
        var before = ReservationReadCount();

        page.Find(".work-order-reservations-edit").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".work-order-reservation-rows > li")));

        page.Find(".work-order-reservations-save").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".work-order-reservation-rows"));
            Assert.True(ReservationReadCount() > before);
        });
    }

    [Fact]
    public void Releasing_asks_for_confirmation_before_calling_the_api()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-release").Click();

        Assert.Contains("Release the remaining part reservations?", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Dismissing_the_release_confirmation_sends_nothing()
    {
        var page = RenderPage();

        page.Find(".work-order-reservations-release").Click();
        page.Find(".work-order-confirm-dismiss").Click();

        Assert.Empty(page.FindAll(".work-order-confirm-card"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_confirmed_release_posts_to_the_release_route_and_reloads()
    {
        var page = RenderPage();
        var before = ReservationReadCount();

        page.Find(".work-order-reservations-release").Click();
        page.Find(".work-order-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                requests,
                request => request.Method == HttpMethod.Post
                    && request.RequestUri!.AbsolutePath
                        == $"/api/maintenance/work-orders/{WorkOrderId}/part-reservations/release");
            Assert.True(ReservationReadCount() > before);
            Assert.Contains("released back to stock", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_release_rejected_as_a_conflict_reports_the_reason_and_reloads()
    {
        respondToRelease = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"The work order is in progress."}""");

        var page = RenderPage();
        var before = ReservationReadCount();

        page.Find(".work-order-reservations-release").Click();
        page.Find(".work-order-confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "The work order is in progress.",
                page.Find(".work-order-details-error").TextContent,
                StringComparison.Ordinal);
            Assert.True(ReservationReadCount() > before);
        });
    }

    [Fact]
    public async Task Leaving_the_page_cancels_the_reservation_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondWithToken = (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("part-reservations", StringComparison.Ordinal))
            {
                observed = token;
                started.TrySetResult();

                return new TaskCompletionSource<HttpResponseMessage>().Task;
            }

            return Json(HttpStatusCode.OK, Details());
        };

        RenderPage();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private int ReservationReadCount()
    {
        return requests.Count(request =>
            request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath
                == $"/api/maintenance/work-orders/{WorkOrderId}/part-reservations");
    }

    private IRenderedComponent<MaintenanceWorkOrderDetails> RenderPage(params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requests.Add(request);

            if (request.Content is not null)
            {
                bodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            if (respondWithToken is not null)
            {
                return await respondWithToken(request, token);
            }

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/part-reservations/release", StringComparison.Ordinal))
            {
                return respondToRelease is not null
                    ? await respondToRelease(request)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/part-reservations", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Put)
                {
                    return respondToReplace is not null
                        ? await respondToReplace(request)
                        : await Json(HttpStatusCode.OK, ReplaceResult());
                }

                return await Json(reservationsStatus, Reservations());
            }

            if (path == "/api/maintenance/inventory-items")
            {
                return await Json(HttpStatusCode.OK, InventoryPage(InventoryItem(), InventoryItem(GasketId, "Gasket", "PN-2")));
            }

            if (path == "/api/maintenance/equipment")
            {
                return await Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":100,"totalCount":0}""");
            }

            return await Json(HttpStatusCode.OK, Details());
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

        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/maintenance/work-orders/{WorkOrderId}");

        var page = Render<MaintenanceWorkOrderDetails>(parameters => parameters
            .Add(component => component.WorkOrderId, Guid.Parse(WorkOrderId)));

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

    private string Details()
    {
        return $$"""
            {"workOrderId":"{{WorkOrderId}}","vesselId":"{{VesselId}}","dryDockId":null,
            "assignedEngineerId":null,"title":"Replace main engine filter",
            "description":"Filter is due for replacement","priority":"High","status":"{{workOrderStatus}}",
            "dueUtc":"2026-12-01T10:00:00Z","createdUtc":"2026-08-01T08:00:00Z",
            "completedUtc":null,"isOverdue":false,"allowedActions":{{allowedActions}},
            "equipment":[],"partUsages":[],"completion":null}
            """;
    }

    private string Reservations()
    {
        return $$"""
            {"workOrderId":"{{WorkOrderId}}","status":"{{workOrderStatus}}",
            "allowedActions":{{allowedActions}},"items":{{reservationItems}},
            "hasShortages":{{(reservationItems.Contains("\"hasShortage\":true", StringComparison.Ordinal) ? "true" : "false")}},
            "retrievedUtc":"2026-08-02T09:00:00Z"}
            """;
    }

    private string ReplaceResult()
    {
        return $$"""
            {"workOrderId":"{{WorkOrderId}}","status":"{{workOrderStatus}}",
            "items":{{reservationItems}},"hasShortages":false,
            "updatedUtc":"2026-08-02T09:00:00Z"}
            """;
    }

    private static string Reservation(
        string inventoryItemId = FilterId,
        string name = "Main engine filter",
        string partNumber = "FILTER-001",
        string quantityRequested = "6",
        string quantityReserved = "6",
        string quantityConsumed = "2",
        string quantityRemaining = "4",
        string shortageQuantity = "0",
        string hasShortage = "false",
        string stockOnHand = "10",
        string availableQuantity = "4")
    {
        return $$"""
            {"inventoryItemId":"{{inventoryItemId}}","name":"{{name}}","partNumber":"{{partNumber}}",
            "quantityRequested":{{quantityRequested}},"quantityReserved":{{quantityReserved}},
            "quantityConsumed":{{quantityConsumed}},"quantityRemaining":{{quantityRemaining}},
            "shortageQuantity":{{shortageQuantity}},"hasShortage":{{hasShortage}},
            "stockOnHand":{{stockOnHand}},"availableQuantity":{{availableQuantity}}}
            """;
    }

    private static string InventoryItem(
        string inventoryItemId = FilterId,
        string name = "Main engine filter",
        string partNumber = "FILTER-001")
    {
        return $$"""
            {"inventoryItemId":"{{inventoryItemId}}","name":"{{name}}","partNumber":"{{partNumber}}",
            "quantity":10,"minimumQuantity":2,"unitCost":150,
            "categoryId":"70000000-0000-0000-0000-000000000007",
            "unitId":"80000000-0000-0000-0000-000000000008",
            "supplierId":"90000000-0000-0000-0000-000000000009"}
            """;
    }

    private static string InventoryPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,
            "pageSize":{{MaintenanceWorkOrderDetails.InventoryPickerPageSize}},
            "totalCount":{{items.Length}}}
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
