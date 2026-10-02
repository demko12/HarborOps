using System.Net;
using System.Text;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Tests.Api;

public sealed class PurchaseOrderClosureApiClientTests
{
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string OrderLineId = "c0000000-0000-0000-0000-00000000000c";
    private const string ItemId = "d0000000-0000-0000-0000-00000000000d";
    private const string ClosureId = "e0000000-0000-0000-0000-00000000000e";
    private const string ConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";
    private const string ClientRequestId = "a1000000-0000-0000-0000-00000000001a";
    private const string ActorUserId = "a2000000-0000-0000-0000-00000000002a";

    private const string OrderPath = $"/api/maintenance/purchase-orders/{OrderId}";

    private const string PreviewJson = $$"""
        {"purchaseOrderId":"{{OrderId}}","orderStatus":"PartiallyReceived","concurrencyToken":"{{ConcurrencyToken}}",
        "previewToken":"TOKEN","canClose":true,"resultingOrderStatus":"ClosedShort",
        "totalQuantityReceived":4,"totalQuantityToClose":6,
        "lines":[{"purchaseOrderLineId":"{{OrderLineId}}","inventoryItemId":"{{ItemId}}",
        "name":"Main engine air filter","partNumber":"FILTER-AIR-110","unitAbbreviation":"pcs",
        "quantityOrdered":10,"receivedQuantity":4,"returnedQuantity":1,"quantityToClose":6}],
        "blockers":[],"generatedUtc":"2026-10-01T08:00:00Z"}
        """;

    private const string ClosureJson = $$"""
        {"closureId":"{{ClosureId}}","purchaseOrderId":"{{OrderId}}","clientRequestId":"{{ClientRequestId}}",
        "reason":"Supplier discontinued the part","closedByUserId":"{{ActorUserId}}",
        "closedUtc":"2026-10-01T08:00:00Z","orderStatusBefore":"PartiallyReceived","orderStatusAfter":"ClosedShort",
        "totalQuantityReceived":4,"totalQuantityClosed":6,
        "lines":[{"closureLineId":"{{ClosureId}}","purchaseOrderLineId":"{{OrderLineId}}","inventoryItemId":"{{ItemId}}",
        "name":"Main engine air filter","partNumber":"FILTER-AIR-110","unitAbbreviation":"pcs",
        "quantityOrdered":10,"receivedQuantity":4,"returnedQuantity":1,"closedQuantity":6}]}
        """;

    private const string CloseResultJson = $$"""
        {"closure":{{ClosureJson}},"alreadyClosed":true,"orderStatus":"ClosedShort",
        "concurrencyToken":"{{ConcurrencyToken}}","allowedActions":["CreateReturn"]}
        """;

    [Fact]
    public async Task GetPurchaseOrderClosurePreviewAsync_reads_received_and_undelivered_quantities()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PreviewJson);
        });

        var result = await CreateClient(httpClient).GetPurchaseOrderClosurePreviewAsync(
            Guid.Parse(OrderId),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"{OrderPath}/close/preview", captured.RequestUri!.AbsolutePath);

        var preview = result.Value!;
        var line = Assert.Single(preview.Lines);

        Assert.True(preview.CanClose);
        Assert.Equal("ClosedShort", preview.ResultingOrderStatus);
        Assert.Equal("TOKEN", preview.PreviewToken);
        Assert.Equal(4m, preview.TotalQuantityReceived);
        Assert.Equal(6m, preview.TotalQuantityToClose);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.QuantityToClose);
    }

    [Fact]
    public async Task ClosePurchaseOrderAsync_sends_the_request_id_tokens_and_reason_and_reads_a_replay()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, CloseResultJson);
        });

        var result = await CreateClient(httpClient).ClosePurchaseOrderAsync(
            Guid.Parse(OrderId),
            new ClosePurchaseOrderRequest
            {
                ClientRequestId = Guid.Parse(ClientRequestId),
                ExpectedConcurrencyToken = Guid.Parse(ConcurrencyToken),
                PreviewToken = "TOKEN",
                Reason = "Supplier discontinued the part",
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"{OrderPath}/close", captured.RequestUri!.AbsolutePath);
        Assert.Contains($"\"clientRequestId\":\"{ClientRequestId}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"expectedConcurrencyToken\":\"{ConcurrencyToken}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"previewToken\":\"TOKEN\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"Supplier discontinued the part\"", body, StringComparison.Ordinal);

        Assert.True(result.IsSuccess);
        Assert.Null(result.RefreshedPreview);

        var closed = result.Value!;

        Assert.True(closed.AlreadyClosed);
        Assert.Equal("ClosedShort", closed.OrderStatus);
        Assert.Equal(["CreateReturn"], closed.AllowedActions);
        Assert.Equal(6m, closed.Closure.TotalQuantityClosed);
        Assert.Equal(6m, Assert.Single(closed.Closure.Lines).ClosedQuantity);
    }

    [Fact]
    public async Task A_stale_close_returns_the_conflict_together_with_the_refreshed_preview()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            $$"""
            {"title":"Purchase order closure preview is out of date.",
            "detail":"The quantities changed after the closure preview.","preview":{{PreviewJson}}}
            """));

        var result = await CreateClient(httpClient).ClosePurchaseOrderAsync(
            Guid.Parse(OrderId),
            new ClosePurchaseOrderRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Purchase order closure preview is out of date.", result.Failure.Title);
        Assert.Equal("The quantities changed after the closure preview.", result.Failure.Message);
        Assert.Equal("TOKEN", result.RefreshedPreview!.PreviewToken);
        Assert.Equal(6m, result.RefreshedPreview.TotalQuantityToClose);
    }

    [Fact]
    public async Task A_conflict_without_a_preview_is_reported_without_one()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Client request id was already used.","detail":"Use a new client request id."}"""));

        var result = await CreateClient(httpClient).ClosePurchaseOrderAsync(
            Guid.Parse(OrderId),
            new ClosePurchaseOrderRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Client request id was already used.", result.Failure.Title);
        Assert.Null(result.RefreshedPreview);
    }

    [Fact]
    public async Task Validation_errors_on_a_close_are_kept_per_field()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"reason":["Reason must be at least 10 characters."]}}"""));

        var result = await CreateClient(httpClient).ClosePurchaseOrderAsync(
            Guid.Parse(OrderId),
            new ClosePurchaseOrderRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(["Reason must be at least 10 characters."], result.Failure.Errors["reason"]);
        Assert.Null(result.RefreshedPreview);
    }

    [Fact]
    public async Task A_close_failure_without_a_body_falls_back_to_the_call_title()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await CreateClient(httpClient).ClosePurchaseOrderAsync(
            Guid.Parse(OrderId),
            new ClosePurchaseOrderRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Unable to close the purchase order", result.Failure.Title);
        Assert.Null(result.RefreshedPreview);
    }

    [Fact]
    public async Task GetPurchaseOrderClosureAsync_reads_the_attributable_closure()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ClosureJson);
        });

        var result = await CreateClient(httpClient).GetPurchaseOrderClosureAsync(
            Guid.Parse(OrderId),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"{OrderPath}/closure", captured.RequestUri!.AbsolutePath);

        var closure = result.Value!;

        Assert.Equal("Supplier discontinued the part", closure.Reason);
        Assert.Equal(Guid.Parse(ActorUserId), closure.ClosedByUserId);
        Assert.Equal("ClosedShort", closure.OrderStatusAfter);
        Assert.Equal(6m, closure.TotalQuantityClosed);
    }

    [Fact]
    public async Task A_missing_closure_is_reported_as_not_found_with_the_call_title()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, "{}"));

        var result = await CreateClient(httpClient).GetPurchaseOrderClosureAsync(
            Guid.Parse(OrderId),
            CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Unable to load the closure", result.Failure.Title);
    }

    [Fact]
    public async Task Every_closure_call_reports_an_unavailable_api()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);
        var orderId = Guid.Parse(OrderId);

        var preview = await client.GetPurchaseOrderClosurePreviewAsync(orderId, CancellationToken.None);
        var close = await client.ClosePurchaseOrderAsync(orderId, new ClosePurchaseOrderRequest(), CancellationToken.None);
        var closure = await client.GetPurchaseOrderClosureAsync(orderId, CancellationToken.None);

        Assert.All(
            new[] { preview.Failure, close.Failure, closure.Failure },
            failure => Assert.Equal(0, failure!.StatusCode));
        Assert.Null(close.RefreshedPreview);
    }

    [Fact]
    public async Task A_cancelled_close_is_not_reported_as_an_unavailable_api()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Json(HttpStatusCode.OK, CloseResultJson);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).ClosePurchaseOrderAsync(
                Guid.Parse(OrderId),
                new ClosePurchaseOrderRequest(),
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
