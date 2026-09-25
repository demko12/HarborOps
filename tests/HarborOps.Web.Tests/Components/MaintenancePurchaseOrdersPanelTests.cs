using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePurchaseOrdersPanelTests : BunitContext
{
    private const string SupplierId = "a0000000-0000-0000-0000-00000000000a";
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string FilterItemId = "c0000000-0000-0000-0000-00000000000c";
    private const string ImpellerItemId = "e0000000-0000-0000-0000-00000000000e";

    private const string OrdersPath = "/api/maintenance/purchase-orders";
    private const string SuppliersPath = "/api/maintenance/suppliers";
    private const string InventoryPath = "/api/maintenance/inventory-items";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = Default;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void Orders_are_listed_with_the_supplier_status_and_total()
    {
        var panel = RenderPanel();

        var row = panel.Find(".procurement-row");

        Assert.Contains("Baltic Marine Parts", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("Draft", panel.Find(".procurement-status").TextContent.Trim());
        Assert.Equal("302.00", panel.Find(".procurement-row .procurement-total").TextContent.Trim());
        Assert.Equal("Due in 6 days", panel.Find(".procurement-delivery-badge").TextContent.Trim());
    }

    [Fact]
    public void Overdue_orders_are_marked_with_text_not_colour_alone()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, OrdersPage(Order(isOverdue: "true", daysUntilDelivery: "-3")))
            : Default(request);

        var panel = RenderPanel();

        Assert.Single(panel.FindAll(".procurement-row.maintenance-overdue-row"));
        Assert.Equal("Overdue", panel.Find(".maintenance-overdue-badge").TextContent.Trim());
        Assert.Equal("Overdue", panel.Find(".procurement-delivery-badge").TextContent.Trim());
    }

    [Fact]
    public void The_supplier_filter_is_populated_from_the_supplier_directory()
    {
        var panel = RenderPanel();

        var options = panel.Find("#po-supplier").QuerySelectorAll("option");

        Assert.Equal(2, options.Length);
        Assert.Equal("Baltic Marine Parts", options[1].TextContent);
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == SuppliersPath);
    }

    [Fact]
    public void Filters_are_sent_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#po-supplier").Change(SupplierId);
        panel.Find("#po-status").Change("Ordered");
        panel.Find("#po-ordered-from").Change("2026-08-01");
        panel.Find("#po-delivery-to").Change("2026-09-30");
        panel.Find("#po-overdue").Change("true");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == OrdersPath).Query;

            Assert.Contains($"supplierId={SupplierId}", query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("status=Ordered", query, StringComparison.Ordinal);
            Assert.Contains("orderDateFrom=2026-08-01", query, StringComparison.Ordinal);
            Assert.Contains("expectedDeliveryTo=2026-09-30", query, StringComparison.Ordinal);
            Assert.Contains("overdue=true", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Items_have_to_be_looked_up_before_a_line_can_reference_them()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-create-toggle").Click();

        Assert.Single(panel.FindAll(".procurement-line-table select option"));

        panel.Find("#create-po-item-search").Change("filter");
        panel.Find(".procurement-item-search-button").Click();

        panel.WaitForAssertion(() =>
        {
            var options = panel.Find(".procurement-line-table select").QuerySelectorAll("option");

            Assert.Equal(3, options.Length);
            Assert.Equal("Fuel filter (PN-1)", options[1].TextContent);

            Assert.Contains(
                "search=filter",
                requestedUris.Last(uri => uri.AbsolutePath == InventoryPath).Query,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Lines_can_be_added_and_removed()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-create-toggle").Click();

        Assert.Single(panel.FindAll(".procurement-line-table tbody tr"));

        panel.Find(".procurement-add-line").Click();
        panel.Find(".procurement-add-line").Click();

        Assert.Equal(3, panel.FindAll(".procurement-line-table tbody tr").Count);

        panel.FindAll(".procurement-line-table tbody tr .col-actions button")[0].Click();

        Assert.Equal(2, panel.FindAll(".procurement-line-table tbody tr").Count);
    }

    [Fact]
    public void Line_and_order_totals_follow_the_entered_quantity_and_cost()
    {
        var panel = RenderPanel();

        OpenCreateFormWithItems(panel);

        SetLine(panel, 0, FilterItemId, "4", "25.5");

        Assert.Equal("102.00", panel.Find(".procurement-line-table tbody .col-line-total").TextContent.Trim());
        Assert.Equal("102.00", panel.Find(".procurement-grand-total").TextContent.Trim());

        panel.Find(".procurement-add-line").Click();
        SetLine(panel, 1, ImpellerItemId, "2", "100");

        Assert.Equal("302.00", panel.Find(".procurement-grand-total").TextContent.Trim());
    }

    [Fact]
    public void The_same_item_on_two_lines_is_flagged_before_submitting()
    {
        var panel = RenderPanel();

        OpenCreateFormWithItems(panel);

        SetLine(panel, 0, FilterItemId, "1", "10");
        panel.Find(".procurement-add-line").Click();
        SetLine(panel, 1, FilterItemId, "2", "10");

        Assert.Equal(2, panel.FindAll(".procurement-line-table .procurement-duplicate-row").Count);
        Assert.Contains(
            "already on another line",
            panel.Find(".procurement-line-table .maintenance-inline-error").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Creating_an_order_posts_the_header_and_the_lines_then_refreshes_the_board()
    {
        respond = request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(
                HttpStatusCode.Created,
                $$"""
                {"purchaseOrderId":"{{OrderId}}","status":"Draft","lineCount":1,"totalCost":102.00}
                """)
            : Default(request);

        var panel = RenderPanel();

        OpenCreateFormWithItems(panel);

        panel.Find("#create-po-supplier").Change(SupplierId);
        panel.Find("#create-po-order-date").Change("2026-08-20");
        panel.Find("#create-po-delivery").Change("2026-09-01");
        SetLine(panel, 0, FilterItemId, "4", "25.5");

        panel.Find(".procurement-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var body = requestBodies.Last();

            Assert.Contains($"\"supplierId\":\"{SupplierId}\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"orderDate\":\"2026-08-20\"", body, StringComparison.Ordinal);
            Assert.Contains("\"expectedDelivery\":\"2026-09-01\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"inventoryItemId\":\"{FilterItemId}\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"quantityOrdered\":4", body, StringComparison.Ordinal);
            Assert.Contains("\"unitCost\":25.5", body, StringComparison.Ordinal);

            Assert.Contains(
                "Draft purchase order was created",
                panel.Find(".procurement-success").TextContent,
                StringComparison.Ordinal);

            Assert.Empty(panel.FindAll(".procurement-form-card"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == OrdersPath) >= 2);
        });
    }

    [Fact]
    public void Creating_an_order_shows_the_validation_errors_returned_by_the_api()
    {
        respond = request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.",
                "errors":{"lines[0].quantityOrdered":["Ordered quantity must be greater than zero."]}}
                """)
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-create-toggle").Click();
        panel.Find(".procurement-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".procurement-field-errors");

            Assert.Contains("Ordered quantity must be greater than zero.", errors.TextContent, StringComparison.Ordinal);
            Assert.Equal("Line 1 quantity", panel.Find(".procurement-field-errors .maintenance-error-field").TextContent);
        });
    }

    [Fact]
    public void Details_show_the_lines_the_total_and_the_allowed_transitions()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            var card = panel.Find(".procurement-details-card");

            Assert.Contains("Baltic Marine Parts", card.TextContent, StringComparison.Ordinal);
            Assert.Contains("Fuel filter", card.TextContent, StringComparison.Ordinal);
            Assert.Contains("PN-1", card.TextContent, StringComparison.Ordinal);
            Assert.Equal("302.00", panel.Find(".procurement-details-card .procurement-grand-total").TextContent.Trim());

            var transitions = panel.FindAll(".procurement-transition").Select(button => button.TextContent.Trim()).ToList();

            Assert.Equal(["Receive stock", "Cancel order"], transitions);
        });
    }

    [Fact]
    public void A_final_order_offers_no_status_actions()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: ""))
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".procurement-transition"));
            Assert.Contains(
                "can no longer change status",
                panel.Find(".procurement-details-card .procurement-hint").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_status_action_is_confirmed_before_anything_is_sent()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();

        Assert.Contains(
            "Inventory quantities will be increased",
            panel.Find(".procurement-confirm").TextContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath.EndsWith("/receive", StringComparison.Ordinal));

        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/receive"));
    }

    [Fact]
    public void Backing_out_of_a_confirmation_sends_nothing()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();
        panel.Find(".procurement-confirm .maintenance-reset").Click();

        Assert.Empty(panel.FindAll(".procurement-confirm"));
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath.EndsWith("/receive", StringComparison.Ordinal));
        Assert.NotEmpty(panel.FindAll(".procurement-transition"));
    }

    [Fact]
    public void Receiving_reports_the_stock_adjustments_and_reloads_the_board()
    {
        var panel = RenderPanel();

        var ordersRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == OrdersPath);

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();
        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "2 inventory quantities were increased",
                panel.Find(".procurement-success").TextContent,
                StringComparison.Ordinal);

            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == OrdersPath) > ordersRequestsBefore);
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}") >= 2);
        });
    }

    [Fact]
    public void Cancelling_an_order_posts_to_the_cancel_action()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.FindAll(".procurement-transition")[1].Click();
        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/cancel"));
    }

    [Fact]
    public void A_rejected_status_change_is_shown_as_a_conflict()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"Purchase order state conflict.",
                "detail":"Purchase order cannot move from status 'Received' to 'Received'."}
                """)
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();
        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "cannot move from status",
            panel.Find(".maintenance-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_status_change_on_a_removed_order_closes_the_details_and_reloads()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.NotFound,
                """{"title":"Purchase order not found.","detail":"It may have been removed."}""")
            : Default(request);

        var panel = RenderPanel();

        var ordersRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == OrdersPath);

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();
        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".procurement-details-card"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == OrdersPath) > ordersRequestsBefore);
        });
    }

    [Fact]
    public void Writing_actions_are_hidden_without_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".procurement-create-toggle"));

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.NotNull(panel.Find(".procurement-details-summary"));
            Assert.Empty(panel.FindAll(".procurement-transition"));
        });
    }

    [Fact]
    public void The_create_form_and_the_details_open_as_modal_dialogs()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-create-toggle").Click();

        var createDialog = panel.Find(".maintenance-modal-backdrop .procurement-form-card");

        Assert.Equal("dialog", createDialog.GetAttribute("role"));
        Assert.Equal("true", createDialog.GetAttribute("aria-modal"));

        panel.Find(".maintenance-modal-backdrop").Click();
        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            var detailsDialog = panel.Find(".maintenance-modal-backdrop .procurement-details-card");

            Assert.Equal("dialog", detailsDialog.GetAttribute("role"));
            Assert.Equal("true", detailsDialog.GetAttribute("aria-modal"));
        });

        panel.Find(".maintenance-modal-backdrop").Click();
        Assert.Empty(panel.FindAll(".maintenance-modal-backdrop"));
    }

    [Fact]
    public void An_empty_board_shows_the_empty_state()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, OrdersPage())
            : Default(request);

        var panel = RenderPanel();

        Assert.Contains(
            "No purchase orders match your filters",
            panel.Find(".empty-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_shows_a_retryable_error()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(
                HttpStatusCode.Forbidden,
                """{"title":"Maintenance access denied","detail":"Missing permission."}""")
            : Default(request);

        var panel = RenderPanel();

        Assert.Contains(
            "Maintenance access denied",
            panel.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
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

        RenderPanelWithoutWaiting();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private void OpenCreateFormWithItems(IRenderedComponent<PurchaseOrdersPanel> panel)
    {
        panel.Find(".procurement-create-toggle").Click();
        panel.Find("#create-po-item-search").Change("p");
        panel.Find(".procurement-item-search-button").Click();

        panel.WaitForAssertion(() => Assert.Equal(
            3,
            panel.Find(".procurement-line-table select").QuerySelectorAll("option").Length));
    }

    private static void SetLine(
        IRenderedComponent<PurchaseOrdersPanel> panel,
        int index,
        string inventoryItemId,
        string quantity,
        string unitCost)
    {
        var row = panel.FindAll(".procurement-line-table tbody tr")[index];

        row.QuerySelector("select")!.Change(inventoryItemId);
        panel.FindAll(".procurement-line-table tbody tr")[index]
            .QuerySelector(".col-line-quantity input")!.Change(quantity);
        panel.FindAll(".procurement-line-table tbody tr")[index]
            .QuerySelector(".col-line-cost input")!.Change(unitCost);
    }

    [Fact]
    public void A_received_order_opens_its_supplier_returns()
    {
        Guid? requestedOrderId = null;
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: ""))
            : Default(request);

        var panel = RenderPanel(parameters => parameters.Add(
            component => component.OnReturnsRequested,
            (Guid orderId) => requestedOrderId = orderId));

        panel.Find(".procurement-details").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".procurement-returns")));

        panel.Find(".procurement-returns").Click();

        Assert.Equal(Guid.Parse(OrderId), requestedOrderId);
        Assert.Empty(panel.FindAll(".procurement-details-card"));
    }

    [Fact]
    public void An_order_that_was_not_received_offers_no_supplier_returns()
    {
        var panel = RenderPanel(parameters => parameters.Add(
            component => component.OnReturnsRequested,
            (Guid _) => { }));

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.NotNull(panel.Find(".procurement-details-card .procurement-line-table"));
            Assert.Empty(panel.FindAll(".procurement-returns"));
        });
    }

    [Fact]
    public void Supplier_returns_are_offered_to_readers_too()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: ""))
            : Default(request);

        var panel = RenderPanel(
            parameters => parameters.Add(component => component.OnReturnsRequested, (Guid _) => { }),
            "maintenance.read");

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".procurement-returns")));
    }

    private IRenderedComponent<PurchaseOrdersPanel> RenderPanel(
        Action<ComponentParameterCollectionBuilder<PurchaseOrdersPanel>> parameterBuilder,
        params string[] permissions)
    {
        var panel = RenderPanelWithoutWaiting(parameterBuilder, permissions);

        panel.WaitForAssertion(() => Assert.Contains(requestedUris, uri => uri.AbsolutePath == OrdersPath));
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".procurement-panel")));

        return panel;
    }

    private IRenderedComponent<PurchaseOrdersPanel> RenderPanel(params string[] permissions)
    {
        var panel = RenderPanelWithoutWaiting(permissions);

        panel.WaitForAssertion(() => Assert.Contains(requestedUris, uri => uri.AbsolutePath == OrdersPath));
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".procurement-panel")));

        return panel;
    }

    private IRenderedComponent<PurchaseOrdersPanel> RenderPanelWithoutWaiting(params string[] permissions)
    {
        return RenderPanelWithoutWaiting(_ => { }, permissions);
    }

    private IRenderedComponent<PurchaseOrdersPanel> RenderPanelWithoutWaiting(
        Action<ComponentParameterCollectionBuilder<PurchaseOrdersPanel>> parameterBuilder,
        params string[] permissions)
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

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        return Render<PurchaseOrdersPanel>(parameterBuilder);
    }

    private static Task<HttpResponseMessage> Default(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path == SuppliersPath)
        {
            return Json(HttpStatusCode.OK, SuppliersPage());
        }

        if (path == InventoryPath)
        {
            return Json(HttpStatusCode.OK, InventoryPage());
        }

        if (path == $"{OrdersPath}/{OrderId}")
        {
            return Json(HttpStatusCode.OK, OrderDetails());
        }

        if (path == $"{OrdersPath}/{OrderId}/receive")
        {
            return Json(HttpStatusCode.OK, StatusResult("Ordered", "Received", withAdjustments: true));
        }

        if (path == $"{OrdersPath}/{OrderId}/order")
        {
            return Json(HttpStatusCode.OK, StatusResult("Draft", "Ordered", withAdjustments: false));
        }

        if (path == $"{OrdersPath}/{OrderId}/cancel")
        {
            return Json(HttpStatusCode.OK, StatusResult("Ordered", "Cancelled", withAdjustments: false));
        }

        return Json(HttpStatusCode.OK, OrdersPage(Order()));
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

    private static string Order(
        string status = "Draft",
        string isOverdue = "false",
        string daysUntilDelivery = "6")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{SupplierId}}",
            "supplierName":"Baltic Marine Parts","orderDate":"2026-08-20","expectedDelivery":"2026-09-01",
            "status":"{{status}}","lineCount":2,"totalCost":302.00,
            "isOverdue":{{isOverdue}},"daysUntilDelivery":{{daysUntilDelivery}}}
            """;
    }

    private static string OrdersPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":25,"totalCount":{{items.Length}}}
            """;
    }

    private static string OrderDetails(
        string status = "Ordered",
        string transitions = "\"Received\",\"Cancelled\"")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{SupplierId}}",
            "supplierName":"Baltic Marine Parts","supplierContactPerson":"Ana Ilic",
            "supplierPhoneEmail":"sales@baltic.test","orderDate":"2026-08-20",
            "expectedDelivery":"2026-09-01","status":"{{status}}","isOverdue":false,
            "daysUntilDelivery":6,"totalCost":302.00,"allowedTransitions":[{{transitions}}],
            "lines":[
            {"lineId":"f0000000-0000-0000-0000-00000000000f","inventoryItemId":"{{FilterItemId}}",
            "inventoryItemName":"Fuel filter","partNumber":"PN-1","quantityOrdered":4,
            "unitCost":25.50,"lineTotal":102.00},
            {"lineId":"f1000000-0000-0000-0000-00000000001f","inventoryItemId":"{{ImpellerItemId}}",
            "inventoryItemName":"Impeller","partNumber":"PN-2","quantityOrdered":2,
            "unitCost":100.00,"lineTotal":200.00}]}
            """;
    }

    private static string StatusResult(string previousStatus, string status, bool withAdjustments)
    {
        var adjustments = withAdjustments
            ? $$"""
                {"inventoryItemId":"{{FilterItemId}}","quantityReceived":4,"quantityOnHand":14},
                {"inventoryItemId":"{{ImpellerItemId}}","quantityReceived":2,"quantityOnHand":5}
                """
            : string.Empty;

        return $$"""
            {"purchaseOrderId":"{{OrderId}}","previousStatus":"{{previousStatus}}","status":"{{status}}",
            "allowedTransitions":[],"stockAdjustments":[{{adjustments}}]}
            """;
    }

    private static string SuppliersPage()
    {
        return $$"""
            {"items":[{"supplierId":"{{SupplierId}}","name":"Baltic Marine Parts",
            "contactPerson":"Ana Ilic","phoneEmail":"sales@baltic.test","address":"Pier 4",
            "paymentTerms":"Net 30"}],"pageNumber":1,"pageSize":100,"totalCount":1}
            """;
    }

    private static string InventoryPage()
    {
        return $$"""
            {"items":[
            {"inventoryItemId":"{{FilterItemId}}","name":"Fuel filter","partNumber":"PN-1",
            "quantity":10,"minimumQuantity":2,"unitCost":25.50,
            "categoryId":"90000000-0000-0000-0000-000000000009",
            "unitId":"91000000-0000-0000-0000-000000000019","supplierId":"{{SupplierId}}"},
            {"inventoryItemId":"{{ImpellerItemId}}","name":"Impeller","partNumber":"PN-2",
            "quantity":3,"minimumQuantity":1,"unitCost":100.00,
            "categoryId":"90000000-0000-0000-0000-000000000009",
            "unitId":"91000000-0000-0000-0000-000000000019","supplierId":"{{SupplierId}}"}],
            "pageNumber":1,"pageSize":100,"totalCount":2}
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
