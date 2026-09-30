using System.Net;
using System.Text;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Tests.Api;

public sealed class PurchaseOrderReceiptApiClientTests
{
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string OrderLineId = "c0000000-0000-0000-0000-00000000000c";
    private const string ItemId = "d0000000-0000-0000-0000-00000000000d";
    private const string ReceiptId = "e0000000-0000-0000-0000-00000000000e";
    private const string ConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";
    private const string ClientRequestId = "a1000000-0000-0000-0000-00000000001a";

    private const string OrderPath = $"/api/maintenance/purchase-orders/{OrderId}";

    private const string PreviewJson = $$"""
        {"purchaseOrderId":"{{OrderId}}","orderStatus":"Ordered","concurrencyToken":"{{ConcurrencyToken}}",
        "previewToken":"TOKEN","canPost":false,"resultingOrderStatus":"PartiallyReceived",
        "lines":[{"purchaseOrderLineId":"{{OrderLineId}}","inventoryItemId":"{{ItemId}}",
        "name":"Main engine air filter","partNumber":"FILTER-AIR-110","unitAbbreviation":"pcs",
        "quantityOrdered":10,"receivedQuantity":4,"outstandingQuantity":6,"quantityToReceive":7,
        "outstandingQuantityAfter":0,"onHandQuantity":12,"onHandQuantityAfter":19}],
        "blockers":[{"purchaseOrderLineId":"{{OrderLineId}}","code":"ExceedsOutstanding",
        "message":"Receiving 7 of 'FILTER-AIR-110' exceeds the outstanding quantity 6."}],
        "generatedUtc":"2026-09-24T08:00:00Z"}
        """;

    private const string ReceiptJson = $$"""
        {"receiptId":"{{ReceiptId}}","purchaseOrderId":"{{OrderId}}","clientRequestId":"{{ClientRequestId}}",
        "receivedUtc":"2026-09-24T06:00:00Z","deliveryReference":"DN-4471","notes":null,
        "recordedByUserId":"{{ItemId}}","recordedUtc":"2026-09-24T08:00:00Z",
        "orderStatusBefore":"Ordered","orderStatusAfter":"PartiallyReceived","totalQuantity":4,
        "lines":[{"receiptLineId":"{{ReceiptId}}","purchaseOrderLineId":"{{OrderLineId}}",
        "inventoryItemId":"{{ItemId}}","name":"Main engine air filter","partNumber":"FILTER-AIR-110",
        "unitAbbreviation":"pcs","quantity":4,"lineReceivedQuantityAfter":4,"outstandingQuantityAfter":6}],
        "movements":[{"movementId":"{{ReceiptId}}","inventoryItemId":"{{ItemId}}",
        "purchaseOrderReceiptId":"{{ReceiptId}}","purchaseOrderReceiptLineId":"{{ReceiptId}}",
        "quantityBefore":8,"quantityAfter":12,"quantityDelta":4,
        "reason":"Received from purchase order, delivery DN-4471","actorUserId":"{{ItemId}}",
        "movedUtc":"2026-09-24T08:00:00Z"}]}
        """;

    private const string PostResultJson = $$"""
        {"receipt":{{ReceiptJson}},"alreadyPosted":true,"orderStatus":"PartiallyReceived",
        "concurrencyToken":"{{ConcurrencyToken}}","allowedActions":["ReceivePartial","ReceiveRemaining","CreateReturn"]}
        """;

    [Fact]
    public async Task GetPurchaseOrderReceiptPreviewAsync_posts_the_entered_lines_and_reads_blockers()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, PreviewJson);
        });

        var result = await CreateClient(httpClient).GetPurchaseOrderReceiptPreviewAsync(
            Guid.Parse(OrderId),
            new PreviewPurchaseOrderReceiptRequest
            {
                ReceivedUtc = new DateTime(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc),
                Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.Parse(OrderLineId), Quantity = 7 }],
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"{OrderPath}/receipts/preview", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"receivedUtc\":\"2026-09-24T06:00:00Z\"", body, StringComparison.Ordinal);
        Assert.Contains(OrderLineId, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"quantity\":7", body, StringComparison.Ordinal);

        var preview = result.Value!;

        Assert.False(preview.CanPost);
        Assert.Equal("PartiallyReceived", preview.ResultingOrderStatus);
        Assert.Equal("TOKEN", preview.PreviewToken);
        Assert.Equal(19m, Assert.Single(preview.Lines).OnHandQuantityAfter);
        Assert.Equal("ExceedsOutstanding", Assert.Single(preview.Blockers).Code);
    }

    [Fact]
    public async Task GetRemainingReceiptPreviewAsync_reads_the_remaining_quantities()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PreviewJson);
        });

        var result = await CreateClient(httpClient).GetRemainingReceiptPreviewAsync(
            Guid.Parse(OrderId),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"{OrderPath}/receive/preview", captured.RequestUri!.AbsolutePath);
        Assert.Equal(6m, Assert.Single(result.Value!.Lines).OutstandingQuantity);
    }

    [Fact]
    public async Task PostPurchaseOrderReceiptAsync_sends_the_request_id_and_tokens_and_reads_a_replay()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, PostResultJson);
        });

        var result = await CreateClient(httpClient).PostPurchaseOrderReceiptAsync(
            Guid.Parse(OrderId),
            new PostPurchaseOrderReceiptRequest
            {
                ClientRequestId = Guid.Parse(ClientRequestId),
                ExpectedConcurrencyToken = Guid.Parse(ConcurrencyToken),
                PreviewToken = "TOKEN",
                ReceivedUtc = new DateTime(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc),
                DeliveryReference = "DN-4471",
                Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.Parse(OrderLineId), Quantity = 4 }],
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"{OrderPath}/receipts", captured.RequestUri!.AbsolutePath);
        Assert.Contains($"\"clientRequestId\":\"{ClientRequestId}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"expectedConcurrencyToken\":\"{ConcurrencyToken}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"previewToken\":\"TOKEN\"", body, StringComparison.Ordinal);
        Assert.Contains("\"deliveryReference\":\"DN-4471\"", body, StringComparison.Ordinal);

        var posted = result.Value!;

        Assert.True(posted.AlreadyPosted);
        Assert.Equal("PartiallyReceived", posted.OrderStatus);
        Assert.Equal(["ReceivePartial", "ReceiveRemaining", "CreateReturn"], posted.AllowedActions);
        Assert.Equal(4m, posted.Receipt.TotalQuantity);
        Assert.Equal(4m, Assert.Single(posted.Receipt.Movements).QuantityDelta);
    }

    [Fact]
    public async Task A_reused_request_id_is_reported_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Receipt request conflicts with an earlier request.","detail":"The request id was used for a different receipt."}"""));

        var result = await CreateClient(httpClient).PostPurchaseOrderReceiptAsync(
            Guid.Parse(OrderId),
            new PostPurchaseOrderReceiptRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Receipt request conflicts with an earlier request.", result.Failure.Title);
        Assert.Equal("The request id was used for a different receipt.", result.Failure.Message);
    }

    [Fact]
    public async Task Validation_errors_on_a_receipt_check_are_kept_per_field()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"lines[0].quantity":["Received quantity must be greater than zero."]}}"""));

        var result = await CreateClient(httpClient).GetPurchaseOrderReceiptPreviewAsync(
            Guid.Parse(OrderId),
            new PreviewPurchaseOrderReceiptRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            ["Received quantity must be greater than zero."],
            result.Failure.Errors!["lines[0].quantity"]);
    }

    [Fact]
    public async Task GetPurchaseOrderReceiptsAsync_sends_the_paging_and_reads_the_page()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, $$"""{"items":[{{ReceiptJson}}],"pageNumber":2,"pageSize":5,"totalCount":6}""");
        });

        var result = await CreateClient(httpClient).GetPurchaseOrderReceiptsAsync(
            Guid.Parse(OrderId),
            2,
            5,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"{OrderPath}/receipts", captured.RequestUri!.AbsolutePath);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=5", captured.RequestUri.Query, StringComparison.Ordinal);

        var page = result.Value!;

        Assert.Equal(6, page.TotalCount);
        Assert.Equal("DN-4471", Assert.Single(page.Items).DeliveryReference);
    }

    [Fact]
    public async Task A_missing_order_is_reported_as_not_found_with_the_call_title()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, "{}"));

        var result = await CreateClient(httpClient).GetPurchaseOrderReceiptsAsync(
            Guid.Parse(OrderId),
            1,
            5,
            CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Unable to load the delivery history", result.Failure.Title);
    }

    [Fact]
    public async Task Every_receipt_call_reports_an_unavailable_api()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);
        var orderId = Guid.Parse(OrderId);

        var preview = await client.GetPurchaseOrderReceiptPreviewAsync(orderId, new PreviewPurchaseOrderReceiptRequest(), CancellationToken.None);
        var remaining = await client.GetRemainingReceiptPreviewAsync(orderId, CancellationToken.None);
        var post = await client.PostPurchaseOrderReceiptAsync(orderId, new PostPurchaseOrderReceiptRequest(), CancellationToken.None);
        var receive = await client.ReceivePurchaseOrderAsync(orderId, new ReceiveRemainingPurchaseOrderRequest(), CancellationToken.None);
        var history = await client.GetPurchaseOrderReceiptsAsync(orderId, 1, 5, CancellationToken.None);

        Assert.All(
            new[]
            {
                preview.Failure,
                remaining.Failure,
                post.Failure,
                receive.Failure,
                history.Failure,
            },
            failure => Assert.Equal(0, failure!.StatusCode));
    }

    [Fact]
    public async Task A_cancelled_receipt_post_is_not_reported_as_an_unavailable_api()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Json(HttpStatusCode.OK, PostResultJson);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).PostPurchaseOrderReceiptAsync(
                Guid.Parse(OrderId),
                new PostPurchaseOrderReceiptRequest(),
                cancellation.Token));
    }

    private static MaintenanceApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
