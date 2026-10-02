using System.Globalization;
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
    private const string FilterLineId = "f0000000-0000-0000-0000-00000000000f";
    private const string ImpellerLineId = "f1000000-0000-0000-0000-00000000001f";
    private const string ReceiptId = "d0000000-0000-0000-0000-00000000000d";
    private const string ConcurrencyToken = "a3000000-0000-0000-0000-00000000003a";

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
    public void The_overdue_kpi_counts_every_page_and_filters_the_board()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath && !IsListRequest(request.RequestUri!)
            ? Json(HttpStatusCode.OK, $$"""{"items":[],"pageNumber":1,"pageSize":1,"totalCount":{{(request.RequestUri!.Query.Contains("overdue=true", StringComparison.Ordinal) ? 9 : 0)}}}""")
            : Default(request);

        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
            Assert.Equal("9", panel.Find("[aria-label='Show overdue purchase orders'] strong").TextContent.Trim()));

        panel.Find("[aria-label='Show overdue purchase orders']").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "overdue=true",
                requestedUris.Last(uri => uri.AbsolutePath == OrdersPath && IsListRequest(uri)).Query,
                StringComparison.Ordinal);
            Assert.Equal("true", panel.Find("[aria-label='Show overdue purchase orders']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void The_status_chart_shows_each_status_and_filters_the_board_from_its_legend()
    {
        respond = request =>
        {
            if (request.RequestUri!.AbsolutePath != OrdersPath || IsListRequest(request.RequestUri!))
            {
                return Default(request);
            }

            var query = request.RequestUri.Query;
            var count = query.Contains("status=Draft", StringComparison.Ordinal) ? 2
                : query.Contains("status=Ordered", StringComparison.Ordinal) ? 5
                : 0;

            return Json(HttpStatusCode.OK, $$"""{"items":[],"pageNumber":1,"pageSize":1,"totalCount":{{count}}}""");
        };

        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(2, panel.FindAll(".procurement-status-mix-donut circle[stroke-dasharray]").Count);
            Assert.Equal("7", panel.Find(".procurement-status-mix-donut text").TextContent.Trim());
        });

        panel.Find("[aria-label='Show ordered purchase orders']").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "status=Ordered",
                requestedUris.Last(uri => uri.AbsolutePath == OrdersPath && IsListRequest(uri)).Query,
                StringComparison.Ordinal);
            Assert.Equal("Ordered", panel.Find("#po-status").GetAttribute("value"));
        });
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
    public void A_partially_received_order_is_labelled_in_words_and_shows_its_progress()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, OrdersPage(Order(status: "PartiallyReceived", quantityReceived: "4")))
            : Default(request);

        var panel = RenderPanel();

        Assert.Equal("Partially received", panel.Find(".procurement-row .procurement-status").TextContent.Trim());
        Assert.Contains("procurement-status-partiallyreceived", panel.Find(".procurement-row .procurement-status").ClassName, StringComparison.Ordinal);
        Assert.Equal("4 / 6", panel.Find(".procurement-row .col-po-received").TextContent.Trim());
    }

    [Fact]
    public void A_draft_order_shows_no_received_progress()
    {
        var panel = RenderPanel();

        Assert.Equal("—", panel.Find(".procurement-row .col-po-received").TextContent.Trim());
    }

    [Fact]
    public void Partially_received_orders_can_be_filtered_by_status()
    {
        var panel = RenderPanel();

        var option = panel.Find("#po-status").QuerySelectorAll("option")
            .Single(candidate => candidate.GetAttribute("value") == "PartiallyReceived");

        Assert.Equal("Partially received", option.TextContent);

        panel.Find("#po-status").Change("PartiallyReceived");

        panel.WaitForAssertion(() => Assert.Contains(
            "status=PartiallyReceived",
            requestedUris.Last(uri => uri.AbsolutePath == OrdersPath && IsListRequest(uri)).Query,
            StringComparison.Ordinal));
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
            var query = requestedUris.Last(uri => uri.AbsolutePath == OrdersPath && IsListRequest(uri)).Query;

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

            var receiptActions = panel.FindAll(".procurement-receipt-action").Select(button => button.TextContent.Trim()).ToList();
            var transitions = panel.FindAll(".procurement-transition").Select(button => button.TextContent.Trim()).ToList();

            Assert.Equal(["Receive delivery", "Receive remaining"], receiptActions);
            Assert.Equal(["Cancel order"], transitions);
        });
    }

    [Fact]
    public void Details_show_received_returned_and_outstanding_quantities_per_line()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(
                HttpStatusCode.OK,
                OrderDetails(
                    status: "PartiallyReceived",
                    transitions: "\"Received\"",
                    actions: "\"ReceivePartial\",\"ReceiveRemaining\",\"CreateReturn\"",
                    filterReceived: 3m,
                    filterReturned: 1m,
                    receiptCount: 2))
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            var filterLine = panel.FindAll(".procurement-details-card .procurement-line-table tbody tr")[0];

            Assert.Equal("3", filterLine.QuerySelector(".col-line-received")!.TextContent.Trim());
            Assert.Equal("1", filterLine.QuerySelector(".col-line-returned")!.TextContent.Trim());
            Assert.Equal("1", filterLine.QuerySelector(".col-line-outstanding")!.TextContent.Trim());
            Assert.Equal("Partially received", panel.Find(".procurement-details-summary .procurement-status").TextContent.Trim());

            var received = panel.Find(".procurement-received-summary").TextContent;

            Assert.Contains("3 of 6 unit(s)", received, StringComparison.Ordinal);
            Assert.Contains("3 outstanding", received, StringComparison.Ordinal);
            Assert.Contains("2 deliveries", received, StringComparison.Ordinal);
            Assert.Contains("1 returned", received, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_legacy_balance_is_marked_on_the_line_and_explained_in_the_history()
    {
        respond = request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"{OrdersPath}/{OrderId}" => Json(
                HttpStatusCode.OK,
                OrderDetails(status: "Received", transitions: "", actions: "\"CreateReturn\"", filterReceived: 4m, filterLegacy: 4m)),
            var path when path == $"{OrdersPath}/{OrderId}/receipts" => Json(HttpStatusCode.OK, ReceiptsPage()),
            _ => Default(request),
        };

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("incl. 4 earlier", panel.Find(".procurement-legacy-note").TextContent, StringComparison.Ordinal);
            Assert.Contains(
                "received before deliveries were tracked",
                panel.Find(".receipt-history").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_delivery_history_is_listed_when_the_order_has_deliveries()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(
                HttpStatusCode.OK,
                OrderDetails(
                    status: "PartiallyReceived",
                    transitions: "\"Received\"",
                    actions: "\"ReceivePartial\",\"ReceiveRemaining\",\"CreateReturn\"",
                    filterReceived: 3m,
                    receiptCount: 1))
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("DN-4471", panel.Find(".receipt-history-table").TextContent, StringComparison.Ordinal);
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/receipts");
        });
    }

    [Fact]
    public void An_order_without_deliveries_does_not_load_a_history()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-details-summary");

        Assert.Empty(panel.FindAll(".receipt-history"));
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/receipts");
    }

    [Fact]
    public void A_partially_received_order_can_receive_the_rest_and_return_goods_but_not_be_cancelled()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(
                HttpStatusCode.OK,
                OrderDetails(
                    status: "PartiallyReceived",
                    transitions: "\"Received\"",
                    actions: "\"ReceivePartial\",\"ReceiveRemaining\",\"CreateReturn\"",
                    filterReceived: 3m,
                    receiptCount: 1))
            : Default(request);

        var panel = RenderPanel(parameters => parameters.Add(component => component.OnReturnsRequested, (Guid _) => { }));

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal(2, panel.FindAll(".procurement-receipt-action").Count);
            Assert.Empty(panel.FindAll(".procurement-transition"));
            Assert.NotEmpty(panel.FindAll(".procurement-returns"));
            Assert.Empty(panel.FindAll(".procurement-details-card .procurement-hint"));
        });
    }

    [Fact]
    public void A_final_order_offers_no_status_actions()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: "", actions: "\"CreateReturn\"", filterReceived: 4m, receiptCount: 1))
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".procurement-transition"));
            Assert.Empty(panel.FindAll(".procurement-receipt-action"));
            Assert.Contains(
                "can no longer change status",
                panel.Find(".procurement-details-card > .procurement-hint").TextContent,
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
            "Inventory quantities stay unchanged",
            panel.Find(".procurement-confirm").TextContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal));

        panel.Find(".procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/cancel"));
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
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal));
        Assert.NotEmpty(panel.FindAll(".procurement-transition"));
    }

    [Fact]
    public void Receiving_a_delivery_opens_the_receipt_form_in_place_of_the_details()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-receipt-action");

        panel.Find(".procurement-receipt-action").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Receive a delivery", panel.Find("#receipt-form-title").TextContent.Trim());
            Assert.Empty(panel.FindAll(".procurement-details-summary"));
            Assert.Equal(2, panel.FindAll(".receipt-lines-table input[type=number]").Count);
        });
    }

    [Fact]
    public void Receiving_the_remaining_quantities_opens_the_remaining_form()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-receipt-action");

        panel.FindAll(".procurement-receipt-action")[1].Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Receive remaining quantities", panel.Find("#receipt-form-title").TextContent.Trim());
            Assert.Empty(panel.FindAll(".receipt-lines-table input[type=number]"));
        });
    }

    [Fact]
    public void Posting_a_delivery_reports_it_and_reloads_the_order_the_history_and_the_board()
    {
        var panel = RenderPanel();

        var ordersRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == OrdersPath);

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-receipt-action");
        panel.Find(".procurement-receipt-action").Click();
        panel.WaitForElement(".receipt-lines-table input[type=number]");

        panel.FindAll(".receipt-lines-table input[type=number]")[0].Change("3");
        panel.Find(".receipt-form .maintenance-button").Click();
        panel.WaitForAssertion(() => Assert.False(panel.Find(".receipt-form .stocktake-post").HasAttribute("disabled")));

        panel.Find(".receipt-form .stocktake-post").Click();
        panel.Find(".receipt-form .procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Receipt posted.", panel.Find(".procurement-success").TextContent, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".receipt-form"));
            Assert.NotEmpty(panel.FindAll(".procurement-details-summary"));
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/receipts");
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == OrdersPath) > ordersRequestsBefore);
        });
    }

    [Fact]
    public void Leaving_the_receipt_form_returns_to_freshly_loaded_details()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-receipt-action");
        panel.Find(".procurement-receipt-action").Click();
        panel.WaitForElement(".receipt-lines-table");

        var detailRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}");

        panel.FindAll(".receipt-form .maintenance-reset").Last().Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".receipt-form"));
            Assert.NotEmpty(panel.FindAll(".procurement-details-summary"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}") > detailRequestsBefore);
        });
    }

    [Fact]
    public void Cancelling_an_order_posts_to_the_cancel_action()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-transition");

        panel.Find(".procurement-transition").Click();
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
            Assert.Empty(panel.FindAll(".procurement-receipt-action"));
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
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: "", actions: "\"CreateReturn\"", filterReceived: 4m))
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
            ? Json(HttpStatusCode.OK, OrderDetails(status: "Received", transitions: "", actions: "\"CreateReturn\"", filterReceived: 4m))
            : Default(request);

        var panel = RenderPanel(
            parameters => parameters.Add(component => component.OnReturnsRequested, (Guid _) => { }),
            "maintenance.read");

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".procurement-returns")));
    }

    [Fact]
    public void A_closed_short_order_is_labelled_in_words_and_shows_what_was_closed()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, OrdersPage(Order(status: "ClosedShort", quantityReceived: "3", quantityClosed: "3")))
            : Default(request);

        var panel = RenderPanel();

        var received = panel.Find(".procurement-row .col-po-received");

        Assert.Equal("Closed short", panel.Find(".procurement-row .procurement-status").TextContent.Trim());
        Assert.Contains("procurement-status-closedshort", panel.Find(".procurement-row .procurement-status").ClassName, StringComparison.Ordinal);
        Assert.Contains("3 / 6", received.TextContent, StringComparison.Ordinal);
        Assert.Equal("3 closed", received.QuerySelector(".procurement-closed-note")!.TextContent.Trim());
    }

    [Fact]
    public void An_order_without_closed_quantity_shows_no_closed_note()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, OrdersPage(Order(status: "PartiallyReceived", quantityReceived: "4")))
            : Default(request);

        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".procurement-closed-note"));
    }

    [Fact]
    public void Closed_short_orders_can_be_filtered_by_status()
    {
        var panel = RenderPanel();

        var option = panel.Find("#po-status").QuerySelectorAll("option")
            .Single(candidate => candidate.GetAttribute("value") == "ClosedShort");

        Assert.Equal("Closed short", option.TextContent);

        panel.Find("#po-status").Change("ClosedShort");

        panel.WaitForAssertion(() => Assert.Contains(
            "status=ClosedShort",
            requestedUris.Last(uri => uri.AbsolutePath == OrdersPath && IsListRequest(uri)).Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_partially_received_order_offers_closing_the_remaining_quantities()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, PartiallyReceivedDetails())
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Close remaining quantities", panel.Find(".procurement-close-remaining").TextContent.Trim());
            Assert.Equal(2, panel.FindAll(".procurement-receipt-action").Count);
            Assert.Empty(panel.FindAll(".procurement-details-card > .procurement-hint"));
        });
    }

    [Fact]
    public void Closing_is_not_offered_to_readers()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, PartiallyReceivedDetails())
            : Default(request);

        var panel = RenderPanel("maintenance.read");

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-details-summary");

        Assert.Empty(panel.FindAll(".procurement-close-remaining"));
    }

    [Fact]
    public void Closing_is_not_offered_when_the_api_does_not_allow_it()
    {
        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-receipt-action");

        Assert.Empty(panel.FindAll(".procurement-close-remaining"));
    }

    [Fact]
    public void Closing_the_remaining_quantities_opens_the_closure_form_in_place_of_the_details()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, PartiallyReceivedDetails())
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-close-remaining");

        panel.Find(".procurement-close-remaining").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Close remaining quantities", panel.Find("#closure-form-title").TextContent.Trim());
            Assert.Empty(panel.FindAll(".procurement-received-summary"));
            Assert.Equal(2, panel.FindAll(".closure-lines-table tbody tr").Count);
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/close/preview");
        });
    }

    [Fact]
    public void Closing_reports_it_and_reloads_the_order_and_the_board()
    {
        var closed = false;
        respond = request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"{OrdersPath}/{OrderId}/close" => Respond(() => closed = true, Json(HttpStatusCode.OK, ClosedResult())),
            var path when path == $"{OrdersPath}/{OrderId}" => Json(HttpStatusCode.OK, closed ? ClosedShortDetails() : PartiallyReceivedDetails()),
            _ => Default(request),
        };

        var panel = RenderPanel();

        var ordersRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == OrdersPath);

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-close-remaining");
        panel.Find(".procurement-close-remaining").Click();
        panel.WaitForElement("#closure-reason");

        panel.Find("#closure-reason").Input("Supplier discontinued the part");
        panel.Find(".closure-form .stocktake-post").Click();
        panel.Find(".closure-form .procurement-confirm .maintenance-button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Order closed short.", panel.Find(".procurement-success").TextContent, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".closure-form"));
            Assert.Equal("Closed short", panel.Find(".procurement-details-summary .procurement-status").TextContent.Trim());
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == OrdersPath) > ordersRequestsBefore);
            Assert.Contains("\"reason\":\"Supplier discontinued the part\"", requestBodies.Last(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Leaving_the_closure_form_returns_to_freshly_loaded_details()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, PartiallyReceivedDetails())
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-close-remaining");
        panel.Find(".procurement-close-remaining").Click();
        panel.WaitForElement(".closure-lines-table");

        var detailRequestsBefore = requestedUris.Count(uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}");

        panel.Find(".closure-form .stocktake-actions .maintenance-reset").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".closure-form"));
            Assert.NotEmpty(panel.FindAll(".procurement-close-remaining"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}") > detailRequestsBefore);
            Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/close");
        });
    }

    [Fact]
    public void A_closed_short_order_shows_its_closed_quantities_and_closure_and_stays_returnable()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, ClosedShortDetails())
            : Default(request);

        var panel = RenderPanel(parameters => parameters.Add(component => component.OnReturnsRequested, (Guid _) => { }));

        panel.Find(".procurement-details").Click();

        panel.WaitForAssertion(() =>
        {
            var filterLine = panel.FindAll(".procurement-details-card .procurement-line-table tbody tr")[0];
            var received = panel.Find(".procurement-received-summary").TextContent;

            Assert.Equal("1", filterLine.QuerySelector(".col-line-closed")!.TextContent.Trim());
            Assert.Equal("0", filterLine.QuerySelector(".col-line-outstanding")!.TextContent.Trim());
            Assert.Contains("0 outstanding", received, StringComparison.Ordinal);
            Assert.Contains("3 closed without delivery", received, StringComparison.Ordinal);
            Assert.Contains("Supplier discontinued the part", panel.Find(".closure-record-reason").TextContent, StringComparison.Ordinal);
            Assert.Contains("closed short", panel.Find(".procurement-details-card > .procurement-hint").TextContent, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".procurement-close-remaining"));
            Assert.Empty(panel.FindAll(".procurement-receipt-action"));
            Assert.NotEmpty(panel.FindAll(".procurement-returns"));
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/closure");
        });
    }

    [Fact]
    public void An_order_that_was_not_closed_does_not_load_a_closure()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrdersPath}/{OrderId}"
            ? Json(HttpStatusCode.OK, PartiallyReceivedDetails())
            : Default(request);

        var panel = RenderPanel();

        panel.Find(".procurement-details").Click();
        panel.WaitForElement(".procurement-details-summary");

        Assert.Empty(panel.FindAll(".closure-record"));
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}/closure");
    }

    private static Task<HttpResponseMessage> Respond(Action sideEffect, Task<HttpResponseMessage> response)
    {
        sideEffect();

        return response;
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

        if (path == $"{OrdersPath}/{OrderId}/receipts/preview" || path == $"{OrdersPath}/{OrderId}/receive/preview")
        {
            return Json(HttpStatusCode.OK, ReceiptPreview());
        }

        if (path == $"{OrdersPath}/{OrderId}/receipts")
        {
            return request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, PostedReceipt())
                : Json(HttpStatusCode.OK, ReceiptsPage(Receipt()));
        }

        if (path == $"{OrdersPath}/{OrderId}/close/preview")
        {
            return Json(HttpStatusCode.OK, ClosurePreview());
        }

        if (path == $"{OrdersPath}/{OrderId}/close")
        {
            return Json(HttpStatusCode.OK, ClosedResult());
        }

        if (path == $"{OrdersPath}/{OrderId}/closure")
        {
            return Json(HttpStatusCode.OK, Closure());
        }

        if (path == $"{OrdersPath}/{OrderId}/order")
        {
            return Json(HttpStatusCode.OK, StatusResult("Draft", "Ordered"));
        }

        if (path == $"{OrdersPath}/{OrderId}/cancel")
        {
            return Json(HttpStatusCode.OK, StatusResult("Ordered", "Cancelled"));
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
        string daysUntilDelivery = "6",
        string quantityReceived = "0",
        string quantityClosed = "0")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{SupplierId}}",
            "supplierName":"Baltic Marine Parts","orderDate":"2026-08-20","expectedDelivery":"2026-09-01",
            "status":"{{status}}","lineCount":2,"totalCost":302.00,
            "isOverdue":{{isOverdue}},"daysUntilDelivery":{{daysUntilDelivery}},
            "quantityOrdered":6,"quantityReceived":{{quantityReceived}},"quantityClosed":{{quantityClosed}},
            "quantityOutstanding":{{6 - decimal.Parse(quantityReceived, CultureInfo.InvariantCulture) - decimal.Parse(quantityClosed, CultureInfo.InvariantCulture)}}}
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
        string transitions = "\"Received\",\"Cancelled\"",
        string actions = "\"ReceivePartial\",\"ReceiveRemaining\",\"Cancel\"",
        decimal filterReceived = 0m,
        decimal filterLegacy = 0m,
        decimal filterReturned = 0m,
        int receiptCount = 0,
        decimal filterClosed = 0m,
        decimal impellerClosed = 0m)
    {
        var filterOutstanding = 4m - filterReceived - filterClosed;

        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{SupplierId}}",
            "supplierName":"Baltic Marine Parts","supplierContactPerson":"Ana Ilic",
            "supplierPhoneEmail":"sales@baltic.test","orderDate":"2026-08-20",
            "expectedDelivery":"2026-09-01","status":"{{status}}","isOverdue":false,
            "daysUntilDelivery":6,"totalCost":302.00,"allowedTransitions":[{{transitions}}],
            "lines":[
            {"lineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}",
            "inventoryItemName":"Fuel filter","partNumber":"PN-1","quantityOrdered":4,
            "unitCost":25.50,"lineTotal":102.00,"receivedQuantity":{{filterReceived}},
            "legacyReceivedQuantity":{{filterLegacy}},"returnedQuantity":{{filterReturned}},"closedQuantity":{{filterClosed}},
            "outstandingQuantity":{{filterOutstanding}},"returnableQuantity":{{filterReceived - filterReturned}}},
            {"lineId":"{{ImpellerLineId}}","inventoryItemId":"{{ImpellerItemId}}",
            "inventoryItemName":"Impeller","partNumber":"PN-2","quantityOrdered":2,
            "unitCost":100.00,"lineTotal":200.00,"receivedQuantity":0,"legacyReceivedQuantity":0,
            "returnedQuantity":0,"closedQuantity":{{impellerClosed}},"outstandingQuantity":{{2m - impellerClosed}},"returnableQuantity":0}],
            "concurrencyToken":"{{ConcurrencyToken}}","totalQuantityOrdered":6,
            "totalQuantityReceived":{{filterReceived}},"totalQuantityReturned":{{filterReturned}},
            "totalQuantityClosed":{{filterClosed + impellerClosed}},
            "totalQuantityOutstanding":{{filterOutstanding + 2m - impellerClosed}},"receiptCount":{{receiptCount}},
            "allowedActions":[{{actions}}]}
            """;
    }

    private static string StatusResult(string previousStatus, string status)
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","previousStatus":"{{previousStatus}}","status":"{{status}}",
            "allowedTransitions":[],"stockAdjustments":[],"receipt":null,"alreadyPosted":false}
            """;
    }

    private static string ClosurePreview()
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","orderStatus":"PartiallyReceived","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"CLOSE-1","canClose":true,"resultingOrderStatus":"ClosedShort",
            "totalQuantityReceived":3,"totalQuantityToClose":3,
            "lines":[{"purchaseOrderLineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}",
            "name":"Fuel filter","partNumber":"PN-1","unitAbbreviation":"pcs","quantityOrdered":4,
            "receivedQuantity":3,"returnedQuantity":0,"quantityToClose":1},
            {"purchaseOrderLineId":"{{ImpellerLineId}}","inventoryItemId":"{{ImpellerItemId}}",
            "name":"Impeller","partNumber":"PN-2","unitAbbreviation":"pcs","quantityOrdered":2,
            "receivedQuantity":0,"returnedQuantity":0,"quantityToClose":2}],
            "blockers":[],"generatedUtc":"2026-10-01T08:00:00Z"}
            """;
    }

    private static string Closure()
    {
        return $$"""
            {"closureId":"a6000000-0000-0000-0000-00000000006a","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","reason":"Supplier discontinued the part",
            "closedByUserId":"a2000000-0000-0000-0000-00000000002a","closedUtc":"2026-10-01T08:00:00Z",
            "orderStatusBefore":"PartiallyReceived","orderStatusAfter":"ClosedShort",
            "totalQuantityReceived":3,"totalQuantityClosed":3,
            "lines":[{"closureLineId":"a7000000-0000-0000-0000-00000000007a","purchaseOrderLineId":"{{FilterLineId}}",
            "inventoryItemId":"{{FilterItemId}}","name":"Fuel filter","partNumber":"PN-1","unitAbbreviation":"pcs",
            "quantityOrdered":4,"receivedQuantity":3,"returnedQuantity":0,"closedQuantity":1}]}
            """;
    }

    private static string ClosedResult()
    {
        return $$"""
            {"closure":{{Closure()}},"alreadyClosed":false,"orderStatus":"ClosedShort",
            "concurrencyToken":"a5000000-0000-0000-0000-00000000005a","allowedActions":["CreateReturn"]}
            """;
    }

    // Received 3 of the 4 fuel filters and none of the impellers yet.
    private static string PartiallyReceivedDetails()
    {
        return OrderDetails(
            status: "PartiallyReceived",
            transitions: "\"Received\"",
            actions: "\"ReceivePartial\",\"ReceiveRemaining\",\"CloseRemaining\",\"CreateReturn\"",
            filterReceived: 3m,
            receiptCount: 1);
    }

    // The same order after its undelivered balance was closed short.
    private static string ClosedShortDetails()
    {
        return OrderDetails(
            status: "ClosedShort",
            transitions: "",
            actions: "\"CreateReturn\"",
            filterReceived: 3m,
            receiptCount: 1,
            filterClosed: 1m,
            impellerClosed: 2m);
    }

    private static string ReceiptPreview(string resultingStatus = "PartiallyReceived")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","orderStatus":"Ordered","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"PREVIEW-1","canPost":true,"resultingOrderStatus":"{{resultingStatus}}",
            "lines":[{"purchaseOrderLineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}",
            "name":"Fuel filter","partNumber":"PN-1","unitAbbreviation":"pcs","quantityOrdered":4,
            "receivedQuantity":0,"outstandingQuantity":4,"quantityToReceive":3,"outstandingQuantityAfter":1,
            "onHandQuantity":10,"onHandQuantityAfter":13}],
            "blockers":[],"generatedUtc":"2026-09-01T08:00:00Z"}
            """;
    }

    private static string Receipt()
    {
        return $$"""
            {"receiptId":"{{ReceiptId}}","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","receivedUtc":"2026-09-01T06:00:00Z",
            "deliveryReference":"DN-4471","notes":null,"recordedByUserId":"a2000000-0000-0000-0000-00000000002a",
            "recordedUtc":"2026-09-01T08:00:00Z","orderStatusBefore":"Ordered","orderStatusAfter":"PartiallyReceived",
            "totalQuantity":3,
            "lines":[{"receiptLineId":"a4000000-0000-0000-0000-00000000004a","purchaseOrderLineId":"{{FilterLineId}}",
            "inventoryItemId":"{{FilterItemId}}","name":"Fuel filter","partNumber":"PN-1","unitAbbreviation":"pcs",
            "quantity":3,"lineReceivedQuantityAfter":3,"outstandingQuantityAfter":1}],
            "movements":[]}
            """;
    }

    private static string ReceiptsPage(params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":1,"pageSize":5,"totalCount":{{items.Length}}}
            """;
    }

    private static string PostedReceipt()
    {
        return $$"""
            {"receipt":{{Receipt()}},"alreadyPosted":false,"orderStatus":"PartiallyReceived",
            "concurrencyToken":"a5000000-0000-0000-0000-00000000005a",
            "allowedActions":["ReceivePartial","ReceiveRemaining","CreateReturn"]}
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

    private static bool IsListRequest(Uri uri)
    {
        return !uri.Query.TrimStart('?').Split('&').Contains("pageSize=1");
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
