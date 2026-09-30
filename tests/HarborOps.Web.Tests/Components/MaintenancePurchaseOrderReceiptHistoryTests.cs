using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePurchaseOrderReceiptHistoryTests : BunitContext
{
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string ReceiptsPath = $"/api/maintenance/purchase-orders/{OrderId}/receipts";

    private readonly List<Uri> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        request => Json(HttpStatusCode.OK, Page(PageNumberOf(request), totalCount: 2));

    [Fact]
    public void Posted_deliveries_are_listed_with_their_lines_and_status_change()
    {
        var history = RenderHistory();

        var rows = history.FindAll(".receipt-history-table tbody tr");

        Assert.Equal(2, rows.Count);
        Assert.Contains("DN-4471", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Second pallet", rows[0].QuerySelector(".receipt-history-notes")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("PN-1: 3 pcs", rows[0].QuerySelector(".receipt-history-lines")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("(1 outstanding after)", rows[0].QuerySelector(".receipt-history-lines")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Ordered → Partially received", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Partially received", rows[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("→", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains($"{ReceiptsPath}?pageNumber=1&pageSize=5", requests.Single().PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void Units_received_before_tracking_are_explained()
    {
        var history = RenderHistory(legacyReceivedQuantity: 2.5m);

        Assert.Contains(
            "2.5 unit(s) were received before deliveries were tracked",
            history.Find(".receipt-history .procurement-hint").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_order_without_deliveries_says_so()
    {
        respond = _ => Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":5,"totalCount":0}""");

        var history = RenderHistory();

        Assert.Contains(
            "No deliveries have been posted for this purchase order yet.",
            history.Find(".receipt-history").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(history.FindAll(".receipt-history-table"));
    }

    [Fact]
    public void Long_histories_are_paged()
    {
        respond = request => Json(HttpStatusCode.OK, Page(PageNumberOf(request), totalCount: 12));

        var history = RenderHistory();

        Assert.Equal("Page 1 of 3", history.Find(".maintenance-page-indicator").TextContent);
        Assert.True(history.Find(".maintenance-pages button").HasAttribute("disabled"));

        history.FindAll(".maintenance-pages button").Last().Click();

        history.WaitForAssertion(() =>
        {
            Assert.Equal("Page 2 of 3", history.Find(".maintenance-page-indicator").TextContent);
            Assert.Contains("pageNumber=2&pageSize=5", requests.Last().Query, StringComparison.Ordinal);
        });

        history.Find(".maintenance-pages button").Click();

        history.WaitForAssertion(() => Assert.Equal("Page 1 of 3", history.Find(".maintenance-page-indicator").TextContent));
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        var attempts = 0;
        respond = request => ++attempts == 1
            ? Json(HttpStatusCode.ServiceUnavailable, """{"title":"Service unavailable."}""")
            : Json(HttpStatusCode.OK, Page(PageNumberOf(request), totalCount: 2));

        var history = RenderHistory(waitForTable: false);

        history.WaitForAssertion(() => Assert.NotEmpty(history.FindAll(".api-error-state")));

        history.Find(".api-error-state button").Click();

        history.WaitForAssertion(() => Assert.Equal(2, history.FindAll(".receipt-history-table tbody tr").Count));
    }

    [Fact]
    public void A_new_refresh_key_reloads_from_the_first_page()
    {
        respond = request => Json(HttpStatusCode.OK, Page(PageNumberOf(request), totalCount: 12));

        var history = RenderHistory();

        history.FindAll(".maintenance-pages button").Last().Click();
        history.WaitForAssertion(() => Assert.Equal("Page 2 of 3", history.Find(".maintenance-page-indicator").TextContent));

        history.Render(parameters => parameters.Add(component => component.RefreshKey, 1));

        history.WaitForAssertion(() =>
        {
            Assert.Equal(3, requests.Count);
            Assert.Contains("pageNumber=1&pageSize=5", requests.Last().Query, StringComparison.Ordinal);
            Assert.Equal("Page 1 of 3", history.Find(".maintenance-page-indicator").TextContent);
        });
    }

    [Fact]
    public void The_same_parameters_do_not_reload()
    {
        var history = RenderHistory();

        history.Render(parameters => parameters.Add(component => component.RefreshKey, 0));

        Assert.Single(requests);
    }

    private IRenderedComponent<PurchaseOrderReceiptHistory> RenderHistory(
        decimal legacyReceivedQuantity = 0,
        bool waitForTable = true)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));

        var history = Render<PurchaseOrderReceiptHistory>(parameters => parameters
            .Add(component => component.PurchaseOrderId, Guid.Parse(OrderId))
            .Add(component => component.LegacyReceivedQuantity, legacyReceivedQuantity)
            .Add(component => component.RefreshKey, 0));

        if (waitForTable)
        {
            history.WaitForAssertion(() => Assert.Empty(history.FindAll(".loading-state")));
        }

        return history;
    }

    private static int PageNumberOf(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query;
        var start = query.IndexOf("pageNumber=", StringComparison.Ordinal) + "pageNumber=".Length;
        var end = query.IndexOf('&', start);

        return int.Parse(end < 0 ? query[start..] : query[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Page(int pageNumber, int totalCount)
    {
        return $$"""
            {"items":[
            {{Receipt("d0000000-0000-0000-0000-00000000000d", "DN-4471", "\"Second pallet\"", "Ordered", "PartiallyReceived")}},
            {{Receipt("d1000000-0000-0000-0000-00000000001d", null, "null", "PartiallyReceived", "PartiallyReceived")}}],
            "pageNumber":{{pageNumber}},"pageSize":5,"totalCount":{{totalCount}}}
            """;
    }

    private static string Receipt(string receiptId, string? deliveryReference, string notes, string before, string after)
    {
        var reference = deliveryReference is null ? "null" : $"\"{deliveryReference}\"";

        return $$"""
            {"receiptId":"{{receiptId}}","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","receivedUtc":"2026-09-01T06:00:00Z",
            "deliveryReference":{{reference}},"notes":{{notes}},"recordedByUserId":"a2000000-0000-0000-0000-00000000002a",
            "recordedUtc":"2026-09-01T08:00:00Z","orderStatusBefore":"{{before}}","orderStatusAfter":"{{after}}",
            "totalQuantity":3,
            "lines":[{"receiptLineId":"e1000000-0000-0000-0000-00000000001e",
            "purchaseOrderLineId":"f0000000-0000-0000-0000-00000000000f",
            "inventoryItemId":"c0000000-0000-0000-0000-00000000000c","name":"Fuel filter","partNumber":"PN-1",
            "unitAbbreviation":"pcs","quantity":3,"lineReceivedQuantityAfter":3,"outstandingQuantityAfter":1}],
            "movements":[]}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
