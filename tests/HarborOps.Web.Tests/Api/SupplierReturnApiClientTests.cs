using System.Net;
using System.Text;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Tests.Api;

public sealed class SupplierReturnApiClientTests
{
    private const string ReturnId = "a0000000-0000-0000-0000-00000000000a";
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string OrderLineId = "c0000000-0000-0000-0000-00000000000c";
    private const string ItemId = "d0000000-0000-0000-0000-00000000000d";
    private const string WorkOrderId = "e0000000-0000-0000-0000-00000000000e";
    private const string ConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";

    private const string ReturnsPageJson = $$"""
        {"items":[{"supplierReturnId":"{{ReturnId}}","purchaseOrderId":"{{OrderId}}",
        "supplierId":"{{ItemId}}","supplierName":"North Sea Marine Supply","status":"Draft",
        "lineCount":2,"totalQuantity":3.5,"createdByUserId":"{{ItemId}}",
        "createdUtc":"2026-09-24T08:00:00Z","postedUtc":null,"cancelledUtc":null,
        "concurrencyToken":"{{ConcurrencyToken}}"}],
        "pageNumber":2,"pageSize":50,"totalCount":1}
        """;

    private const string DetailsJson = $$"""
        {"supplierReturnId":"{{ReturnId}}","purchaseOrderId":"{{OrderId}}",
        "purchaseOrderStatus":"Received","supplierId":"{{ItemId}}",
        "supplierName":"North Sea Marine Supply","status":"Posted","notes":"RMA 1042",
        "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{ItemId}}",
        "createdUtc":"2026-09-24T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
        "postedByUserId":"{{ItemId}}","postedUtc":"2026-09-24T09:00:00Z",
        "cancelledByUserId":null,"cancelledUtc":null,"cancellationReason":null,
        "lines":[{"supplierReturnLineId":"{{WorkOrderId}}","purchaseOrderLineId":"{{OrderLineId}}",
        "inventoryItemId":"{{ItemId}}","name":"Main engine air filter","partNumber":"FILTER-AIR-110",
        "unitAbbreviation":"pcs","receivedQuantity":10,"previouslyReturnedQuantity":1,
        "remainingReturnableQuantity":6.5,"onHandQuantity":12,"reservedQuantity":2,
        "quantity":2.5,"reason":"Wrong size"}],
        "blockers":[],
        "movements":[{"movementId":"{{WorkOrderId}}","inventoryItemId":"{{ItemId}}",
        "supplierReturnId":"{{ReturnId}}","supplierReturnLineId":"{{WorkOrderId}}",
        "quantityBefore":14.5,"quantityAfter":12,"quantityDelta":-2.5,"reason":"Wrong size",
        "actorUserId":"{{ItemId}}","movedUtc":"2026-09-24T09:00:00Z"}]}
        """;

    private const string PreviewJson = $$"""
        {"supplierReturnId":"{{ReturnId}}","concurrencyToken":"{{ConcurrencyToken}}",
        "previewToken":"TOKEN","canPost":false,
        "lines":[],
        "items":[{"inventoryItemId":"{{ItemId}}","name":"Main engine air filter",
        "partNumber":"FILTER-AIR-110","onHandQuantity":10,"reservedQuantity":8,
        "availableQuantity":2,"returnQuantity":3,"quantityAfter":7}],
        "blockers":[{"purchaseOrderLineId":null,"inventoryItemId":"{{ItemId}}","code":"ReservedStock",
        "message":"Returning 3.000 would consume stock reserved for maintenance.",
        "reservingWorkOrderIds":["{{WorkOrderId}}"]}],
        "generatedUtc":"2026-09-24T08:00:00Z"}
        """;

    private const string PostResultJson = $$"""
        {"supplierReturnId":"{{ReturnId}}","status":"Posted","concurrencyToken":"{{ConcurrencyToken}}",
        "postedByUserId":"{{ItemId}}","postedUtc":"2026-09-24T09:00:00Z","alreadyPosted":true,
        "movements":[]}
        """;

    [Fact]
    public async Task GetSupplierReturnsAsync_sends_the_status_order_filter_and_paging()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ReturnsPageJson);
        });

        var result = await CreateClient(httpClient).GetSupplierReturnsAsync(
            new SupplierReturnFilter("Draft", Guid.Parse(OrderId), 2, 50),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/maintenance/supplier-returns", captured.RequestUri!.AbsolutePath);
        Assert.Contains("status=Draft", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"purchaseOrderId={OrderId}", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal("North Sea Marine Supply", row.SupplierName);
        Assert.Equal(2, row.LineCount);
        Assert.Equal(3.5m, row.TotalQuantity);
        Assert.Equal(Guid.Parse(ConcurrencyToken), row.ConcurrencyToken);
    }

    [Fact]
    public async Task GetSupplierReturnsAsync_omits_unset_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, ReturnsPageJson);
        });

        await CreateClient(httpClient).GetSupplierReturnsAsync(
            new SupplierReturnFilter(null, null, 1, 25),
            CancellationToken.None);

        Assert.DoesNotContain("status=", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("purchaseOrderId=", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSupplierReturnAsync_reads_the_line_allowances_and_movements()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await CreateClient(httpClient).GetSupplierReturnAsync(Guid.Parse(ReturnId), CancellationToken.None);

        Assert.Equal($"/api/maintenance/supplier-returns/{ReturnId}", captured!.RequestUri!.AbsolutePath);

        var details = result.Value!;
        var line = Assert.Single(details.Lines);
        var movement = Assert.Single(details.Movements);

        Assert.Equal("Received", details.PurchaseOrderStatus);
        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(1m, line.PreviouslyReturnedQuantity);
        Assert.Equal(6.5m, line.RemainingReturnableQuantity);
        Assert.Equal(2m, line.ReservedQuantity);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(14.5m, movement.QuantityBefore);
        Assert.Equal(12m, movement.QuantityAfter);
        Assert.Equal(-2.5m, movement.QuantityDelta);
    }

    [Fact]
    public async Task GetSupplierReturnAsync_reports_an_unknown_return()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Supplier return not found.","detail":"No such return."}"""));

        var result = await CreateClient(httpClient).GetSupplierReturnAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Supplier return not found.", result.Failure.Title);
    }

    [Fact]
    public async Task CreateSupplierReturnAsync_posts_the_order_and_lines_and_reads_the_draft()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.Created, DetailsJson);
        });

        var result = await CreateClient(httpClient).CreateSupplierReturnAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = Guid.Parse(OrderId),
                Notes = "RMA 1042",
                Lines =
                [
                    new SupplierReturnLineEntry
                    {
                        PurchaseOrderLineId = Guid.Parse(OrderLineId),
                        Quantity = 2.5m,
                        Reason = "Wrong size",
                    },
                ],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/maintenance/supplier-returns", captured.RequestUri!.AbsolutePath);
        Assert.Contains(OrderId, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(OrderLineId, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"quantity\":2.5", body, StringComparison.Ordinal);
        Assert.Contains("Wrong size", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSupplierReturnAsync_surfaces_line_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """
            {"title":"One or more validation errors occurred.","status":400,
            "errors":{"lines[0].quantity":["Return quantity exceeds the remaining returnable quantity 0."]}}
            """));

        var result = await CreateClient(httpClient).CreateSupplierReturnAsync(
            new CreateSupplierReturnRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(
            ["Return quantity exceeds the remaining returnable quantity 0."],
            result.Failure.Errors["lines[0].quantity"]);
    }

    [Fact]
    public async Task CreateSupplierReturnAsync_reports_an_order_that_was_not_received()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Purchase order has not been received.","detail":"Only received orders can be returned."}"""));

        var result = await CreateClient(httpClient).CreateSupplierReturnAsync(
            new CreateSupplierReturnRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Purchase order has not been received.", result.Failure.Title);
    }

    [Fact]
    public async Task UpdateSupplierReturnAsync_puts_the_expected_concurrency_token()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await CreateClient(httpClient).UpdateSupplierReturnAsync(
            Guid.Parse(ReturnId),
            new UpdateSupplierReturnRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(ConcurrencyToken),
                Lines = [],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/maintenance/supplier-returns/{ReturnId}", captured.RequestUri!.AbsolutePath);
        Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSupplierReturnPreviewAsync_reads_the_per_part_stock_and_blockers()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PreviewJson);
        });

        var result = await CreateClient(httpClient).GetSupplierReturnPreviewAsync(
            Guid.Parse(ReturnId),
            CancellationToken.None);

        Assert.Equal($"/api/maintenance/supplier-returns/{ReturnId}/preview", captured!.RequestUri!.AbsolutePath);

        var preview = result.Value!;
        var item = Assert.Single(preview.Items);
        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal("TOKEN", preview.PreviewToken);
        Assert.Equal(2m, item.AvailableQuantity);
        Assert.Equal(7m, item.QuantityAfter);
        Assert.Null(blocker.PurchaseOrderLineId);
        Assert.Equal("ReservedStock", blocker.Code);
        Assert.Equal([Guid.Parse(WorkOrderId)], blocker.ReservingWorkOrderIds);
    }

    [Fact]
    public async Task PostSupplierReturnAsync_sends_both_tokens_and_reads_a_repeated_post()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, PostResultJson);
        });

        var result = await CreateClient(httpClient).PostSupplierReturnAsync(
            Guid.Parse(ReturnId),
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(ConcurrencyToken),
                PreviewToken = "TOKEN",
            },
            CancellationToken.None);

        Assert.Equal($"/api/maintenance/supplier-returns/{ReturnId}/post", captured!.RequestUri!.AbsolutePath);
        Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TOKEN", body, StringComparison.Ordinal);
        Assert.True(result.Value!.AlreadyPosted);
    }

    [Fact]
    public async Task PostSupplierReturnAsync_reports_a_stale_preview_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Supplier return posting preview is out of date.","detail":"Review a new preview."}"""));

        var result = await CreateClient(httpClient).PostSupplierReturnAsync(
            Guid.Parse(ReturnId),
            new PostSupplierReturnRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Review a new preview.", result.Failure.Message);
    }

    [Fact]
    public async Task CancelSupplierReturnAsync_posts_the_reason()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return await Json(HttpStatusCode.OK, DetailsJson);
        });

        await CreateClient(httpClient).CancelSupplierReturnAsync(
            Guid.Parse(ReturnId),
            new CancelSupplierReturnRequest
            {
                ExpectedConcurrencyToken = Guid.Parse(ConcurrencyToken),
                Reason = "Supplier issued a credit",
            },
            CancellationToken.None);

        Assert.Equal($"/api/maintenance/supplier-returns/{ReturnId}/cancel", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("Supplier issued a credit", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forbidden_response_is_reported_as_forbidden()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).GetSupplierReturnsAsync(
            new SupplierReturnFilter(null, null, 1, 25),
            CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task Every_supplier_return_call_reports_an_unavailable_api()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);
        var returnId = Guid.Parse(ReturnId);

        var list = await client.GetSupplierReturnsAsync(new SupplierReturnFilter(null, null, 1, 25), CancellationToken.None);
        var details = await client.GetSupplierReturnAsync(returnId, CancellationToken.None);
        var create = await client.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(), CancellationToken.None);
        var update = await client.UpdateSupplierReturnAsync(returnId, new UpdateSupplierReturnRequest(), CancellationToken.None);
        var preview = await client.GetSupplierReturnPreviewAsync(returnId, CancellationToken.None);
        var post = await client.PostSupplierReturnAsync(returnId, new PostSupplierReturnRequest(), CancellationToken.None);
        var cancel = await client.CancelSupplierReturnAsync(returnId, new CancelSupplierReturnRequest(), CancellationToken.None);

        Assert.All(
            new[]
            {
                list.Failure,
                details.Failure,
                create.Failure,
                update.Failure,
                preview.Failure,
                post.Failure,
                cancel.Failure,
            },
            failure => Assert.Equal(0, failure!.StatusCode));
    }

    [Fact]
    public async Task A_cancelled_request_is_not_reported_as_an_unavailable_api()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Json(HttpStatusCode.OK, ReturnsPageJson);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(httpClient).GetSupplierReturnsAsync(
                new SupplierReturnFilter(null, null, 1, 25),
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
