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

public sealed class MaintenanceSupplierReturnsPanelTests : BunitContext
{
    private const string ReturnId = "a0000000-0000-0000-0000-00000000000a";
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string OtherOrderId = "b1000000-0000-0000-0000-00000000001b";
    private const string FilterLineId = "c0000000-0000-0000-0000-00000000000c";
    private const string FilterId = "d0000000-0000-0000-0000-00000000000d";
    private const string ConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";

    private const string ReturnsPath = "/api/maintenance/supplier-returns";
    private const string OrdersPath = "/api/maintenance/purchase-orders";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = DefaultResponse;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_list_is_requested_with_the_default_page()
    {
        RenderPanel();

        var query = requestedUris.Last(IsListRequest);

        Assert.Equal(ReturnsPath, query.AbsolutePath);
        Assert.Contains("pageNumber=1", query.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", query.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("purchaseOrderId=", query.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_return_is_listed_with_its_order_supplier_and_quantity()
    {
        var panel = RenderPanel();

        var cells = panel.Find(".supplier-return-table tbody tr").QuerySelectorAll("td");

        Assert.Equal("PO b0000000", cells[1].TextContent.Trim());
        Assert.Equal("North Sea Marine Supply", cells[2].TextContent.Trim());
        Assert.Equal("Draft", cells[3].TextContent.Trim());
        Assert.Equal("2", cells[4].TextContent.Trim());
        Assert.Equal("3.5", cells[5].TextContent.Trim());
    }

    [Theory]
    [InlineData("Draft", "pending")]
    [InlineData("Posted", "done")]
    [InlineData("Cancelled", "cancelled")]
    public void The_status_is_shown_as_a_badge(string status, string modifier)
    {
        respond = _ => Json(HttpStatusCode.OK, ReturnsPage(Summary(status: status)));

        var panel = RenderPanel();

        var badge = panel.Find(".stocktake-status");

        Assert.Contains(modifier, badge.ClassName, StringComparison.Ordinal);
        Assert.Equal(status, badge.TextContent.Trim());
    }

    [Fact]
    public void The_summary_counts_drafts_and_the_posted_quantity()
    {
        respond = request => IsListRequest(request.RequestUri!)
            ? Json(
                HttpStatusCode.OK,
                ReturnsPage(
                    Summary(totalQuantity: "1"),
                    Summary(status: "Posted", totalQuantity: "2.5"),
                    Summary(status: "Cancelled", totalQuantity: "4")))
            : Json(HttpStatusCode.OK, Page(1, 1, 6));

        var panel = RenderPanel();

        var totals = panel.FindAll(".maintenance-summary-card strong");

        Assert.Equal("3", totals[0].TextContent.Trim());
        panel.WaitForAssertion(() => Assert.Equal("6", panel.FindAll(".maintenance-summary-card strong")[1].TextContent.Trim()));
        Assert.Equal("2.5", totals[2].TextContent.Trim());
    }

    [Fact]
    public void An_empty_list_explains_where_returns_come_from()
    {
        respond = _ => Json(HttpStatusCode.OK, ReturnsPage());

        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".supplier-return-table"));
        Assert.Contains("No supplier returns yet", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_asks_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Broken"}""");

        var panel = RenderPanel();
        var before = requestedUris.Count;

        respond = DefaultResponse;
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(requestedUris.Count > before);
            Assert.NotNull(panel.Find(".supplier-return-table"));
        });
    }

    [Fact]
    public void A_forbidden_list_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"No access"}""");

        var panel = RenderPanel();

        Assert.Contains("You cannot view supplier returns", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".api-error-state"));
    }

    [Fact]
    public void Filtering_by_status_reloads_from_the_first_page()
    {
        var panel = RenderPanel();

        panel.Find("#supplier-return-status").Change("Posted");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(IsListRequest).Query;

            Assert.Contains("status=Posted", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_asks_for_the_next_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 25, 60, Summary()));

        var panel = RenderPanel();

        panel.FindAll(".maintenance-pages button")[1].Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "pageNumber=2",
            requestedUris.Last(IsListRequest).Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Arriving_from_an_order_filters_the_list_and_shows_the_order_chip()
    {
        var panel = RenderPanel(Guid.Parse(OrderId));

        Assert.Contains($"purchaseOrderId={OrderId}", requestedUris.Last(IsListRequest).Query, StringComparison.Ordinal);
        Assert.Contains("PO b0000000", panel.Find(".supplier-return-order-filter").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_the_order_chip_lists_returns_for_every_order()
    {
        var panel = RenderPanel(Guid.Parse(OrderId));

        panel.Find(".supplier-return-order-filter button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("purchaseOrderId=", requestedUris.Last(IsListRequest).Query, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".supplier-return-order-filter"));
        });
    }

    [Fact]
    public void The_read_only_planner_cannot_start_a_return_and_can_only_view()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".supplier-return-new"));
        Assert.Equal("View", panel.Find(".supplier-return-table tbody tr button").TextContent.Trim());
    }

    [Fact]
    public void A_manager_can_edit_a_draft_but_only_view_a_posted_return()
    {
        respond = _ => Json(HttpStatusCode.OK, ReturnsPage(Summary(), Summary(status: "Posted")));

        var panel = RenderManagerPanel();

        var actions = panel.FindAll(".supplier-return-table tbody tr button").Select(button => button.TextContent.Trim());

        Assert.Equal(["Edit", "View"], actions);
    }

    [Fact]
    public void Starting_a_return_without_an_order_offers_only_received_orders()
    {
        var panel = RenderManagerPanel();

        panel.Find(".supplier-return-new").Click();

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == OrdersPath).Query;

            Assert.Contains("status=Received", query, StringComparison.Ordinal);
            Assert.Contains("North Sea Marine Supply", panel.Find(".supplier-return-order-picker").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_received_orders_means_nothing_can_be_returned()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrdersPath
            ? Json(HttpStatusCode.OK, Page(1, 10, 0))
            : DefaultResponse(request);

        var panel = RenderManagerPanel();

        panel.Find(".supplier-return-new").Click();

        panel.WaitForAssertion(() =>
        {
            var picker = panel.Find(".supplier-return-order-picker").TextContent;

            Assert.Contains("No purchase orders with this status", picker, StringComparison.Ordinal);
            Assert.Contains("Only parts that were actually received can be returned", picker, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Partially_received_orders_can_be_picked_for_a_return()
    {
        var panel = RenderManagerPanel();

        panel.Find(".supplier-return-new").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".supplier-return-order-picker tbody button")));

        panel.Find("#supplier-return-order-status").Change("PartiallyReceived");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last(uri => uri.AbsolutePath == OrdersPath).Query;

            Assert.Contains("status=PartiallyReceived", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Picking_a_received_order_opens_a_new_draft_for_it()
    {
        var panel = RenderManagerPanel();
        panel.Find(".supplier-return-new").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".supplier-return-order-picker tbody button")));

        panel.Find(".supplier-return-order-picker tbody button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("New supplier return", panel.Find("#supplier-return-editor-title").TextContent.Trim());
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OtherOrderId}");
        });
    }

    [Fact]
    public void A_filtered_list_starts_the_new_return_for_that_order_directly()
    {
        var panel = RenderPanel(Guid.Parse(OrderId), "maintenance.read", "maintenance.manage");

        var button = panel.Find(".supplier-return-new");

        Assert.Equal("New return for this order", button.TextContent.Trim());

        button.Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Empty(panel.FindAll(".supplier-return-order-picker"));
            Assert.Contains(requestedUris, uri => uri.AbsolutePath == $"{OrdersPath}/{OrderId}");
            Assert.NotNull(panel.Find(".supplier-return-editor"));
        });
    }

    [Fact]
    public void Opening_a_return_shows_the_editor_and_closing_reloads_the_list()
    {
        var panel = RenderManagerPanel();
        var listLoads = requestedUris.Count(uri => uri.AbsolutePath == ReturnsPath);

        panel.Find(".supplier-return-table tbody tr button").Click();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".supplier-return-lines-table")));

        panel.Find(".supplier-return-editor .maintenance-card-heading .maintenance-reset").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.NotNull(panel.Find(".supplier-return-table"));
            Assert.True(requestedUris.Count(uri => uri.AbsolutePath == ReturnsPath) > listLoads);
        });
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

        RenderPanel();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<SupplierReturnsPanel> RenderManagerPanel()
    {
        return RenderPanel((Guid?)null, "maintenance.read", "maintenance.manage");
    }

    private IRenderedComponent<SupplierReturnsPanel> RenderPanel(params string[] permissions)
    {
        return RenderPanel(null, permissions);
    }

    private IRenderedComponent<SupplierReturnsPanel> RenderPanel(Guid? purchaseOrderId, params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondWithToken is not null
                ? await respondWithToken(request, token)
                : await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.read"] : permissions));

        var panel = Render<SupplierReturnsPanel>(parameters =>
            parameters.Add(component => component.PurchaseOrderId, purchaseOrderId));
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

    private static Task<HttpResponseMessage> DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path == OrdersPath)
        {
            return Json(HttpStatusCode.OK, Page(1, 10, 1, OrderSummary()));
        }

        if (path.StartsWith(OrdersPath + "/", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, OrderDetails(path[(OrdersPath.Length + 1)..]));
        }

        if (path.EndsWith("/preview", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, Preview());
        }

        if (path == $"{ReturnsPath}/{ReturnId}")
        {
            return Json(HttpStatusCode.OK, Details());
        }

        return Json(HttpStatusCode.OK, ReturnsPage(Summary()));
    }

    private static string Summary(string status = "Draft", string totalQuantity = "3.5")
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","purchaseOrderId":"{{OrderId}}","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","status":"{{status}}","lineCount":2,
            "totalQuantity":{{totalQuantity}},"createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-24T08:00:00Z","postedUtc":null,"cancelledUtc":null,
            "concurrencyToken":"{{ConcurrencyToken}}"}
            """;
    }

    private static string OrderSummary()
    {
        return $$"""
            {"purchaseOrderId":"{{OtherOrderId}}","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","orderDate":"2026-09-01","expectedDelivery":null,
            "status":"Received","lineCount":1,"totalCost":200.00,"isOverdue":false,"daysUntilDelivery":null}
            """;
    }

    private static string OrderDetails(string purchaseOrderId)
    {
        return $$"""
            {"purchaseOrderId":"{{purchaseOrderId}}","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","supplierContactPerson":null,
            "supplierPhoneEmail":null,"orderDate":"2026-09-01","expectedDelivery":null,
            "status":"Received","isOverdue":false,"daysUntilDelivery":null,"totalCost":200.00,
            "allowedTransitions":[],
            "lines":[{"lineId":"{{FilterLineId}}","inventoryItemId":"{{FilterId}}",
            "inventoryItemName":"Main engine air filter","partNumber":"FILTER-AIR-110",
            "quantityOrdered":10,"unitCost":20.00,"lineTotal":200.00}]}
            """;
    }

    private static string Details()
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","purchaseOrderId":"{{OrderId}}",
            "purchaseOrderStatus":"Received","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","status":"Draft","notes":null,
            "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-24T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
            "postedByUserId":null,"postedUtc":null,"cancelledByUserId":null,"cancelledUtc":null,
            "cancellationReason":null,
            "lines":[{"supplierReturnLineId":"{{ReturnId}}","purchaseOrderLineId":"{{FilterLineId}}",
            "inventoryItemId":"{{FilterId}}","name":"Main engine air filter","partNumber":"FILTER-AIR-110",
            "unitAbbreviation":"pcs","receivedQuantity":10,"previouslyReturnedQuantity":0,
            "remainingReturnableQuantity":10,"onHandQuantity":12,"reservedQuantity":0,
            "quantity":2,"reason":"Wrong size"}],
            "blockers":[],"movements":[]}
            """;
    }

    private static string Preview()
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"TOKEN","canPost":true,"lines":[],"items":[],"blockers":[],
            "generatedUtc":"2026-09-24T08:00:00Z"}
            """;
    }

    private static string ReturnsPage(params string[] items)
    {
        return Page(1, 25, items.Length, items);
    }

    private static bool IsListRequest(Uri uri)
    {
        return !uri.Query.TrimStart('?').Split('&').Contains("pageSize=1");
    }

    private static string Page(int pageNumber, int pageSize, int totalCount, params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":{{pageNumber}},
            "pageSize":{{pageSize}},"totalCount":{{totalCount}}}
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
