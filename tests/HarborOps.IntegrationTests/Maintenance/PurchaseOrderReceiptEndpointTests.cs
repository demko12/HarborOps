using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class PurchaseOrderReceiptEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public PurchaseOrderReceiptEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    // The maintenance module runs on the system clock, so receipt times are taken relative to real time.
    private static DateTime ReceivedUtc => DateTime.UtcNow.AddHours(-1);

    [Fact]
    public async Task Preview_predicts_the_receipt_for_a_reader_without_changing_anything()
    {
        using var client = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 3m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId, "/preview"),
            Body(seed, (0, 4m)),
            CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.True(preview.CanPost);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), preview.ResultingOrderStatus);
        Assert.Equal(seed.ConcurrencyToken, preview.ConcurrencyToken);

        var line = Assert.Single(preview.Lines);

        Assert.Equal(6m, line.OutstandingQuantityAfter);
        Assert.Equal(7m, line.OnHandQuantityAfter);
        Assert.Equal(3m, await StoredQuantityAsync(seed.ItemIds[0]));
        Assert.Equal(0, await ReceiptCountAsync(seed.OrderId));
    }

    [Fact]
    public async Task Preview_reports_over_receipt_as_a_blocker()
    {
        using var client = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId, "/preview"),
            Body(seed, (0, 11m)),
            CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.False(preview.CanPost);
        Assert.Equal(PurchaseOrderReceiptBlockerCodes.ExceedsOutstanding, Assert.Single(preview.Blockers).Code);
    }

    [Fact]
    public async Task Preview_of_the_remaining_quantity_lists_only_outstanding_lines()
    {
        using var client = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.PartiallyReceived, quantityOnHand: 0m, 10m, 2m);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive/preview", UriKind.Relative),
            CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), preview.ResultingOrderStatus);
        Assert.Equal(
            [5m, 2m],
            preview.Lines.OrderByDescending(line => line.QuantityOrdered).Select(line => line.QuantityToReceive));
    }

    [Fact]
    public async Task Receiving_four_then_six_of_ten_closes_the_order_and_adds_stock_once_per_delivery()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var firstResponse = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            await ConfirmedBodyAsync(client, seed, (0, 4m)) with { DeliveryReference = "DN-1" },
            CancellationToken.None);
        var first = await firstResponse.Content.ReadFromJsonAsync<PostPurchaseOrderReceiptResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), first.OrderStatus);
        Assert.NotEqual(Guid.Empty, first.Receipt.RecordedByUserId);
        Assert.Equal("DN-1", first.Receipt.DeliveryReference);
        Assert.Equal(6m, Assert.Single(first.Receipt.Lines).OutstandingQuantityAfter);
        Assert.Equal(4m, Assert.Single(first.Receipt.Movements).QuantityDelta);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));

        var details = await GetDetailsAsync(client, seed.OrderId);

        Assert.Equal(4m, details.TotalQuantityReceived);
        Assert.Equal(6m, details.TotalQuantityOutstanding);
        Assert.Equal(1, details.ReceiptCount);

        using var secondResponse = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            await ConfirmedBodyAsync(client, seed, (0, 6m)),
            CancellationToken.None);
        var second = await secondResponse.Content.ReadFromJsonAsync<PostPurchaseOrderReceiptResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(second);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), second.OrderStatus);
        Assert.Equal(10m, await StoredQuantityAsync(seed.ItemIds[0]));
        Assert.Equal(PurchaseOrderStatus.Received, await StoredStatusAsync(seed.OrderId));
    }

    [Fact]
    public async Task Posting_an_identical_request_again_returns_the_original_receipt()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var body = await ConfirmedBodyAsync(client, seed, (0, 4m));

        using var firstResponse = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), body, CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), body, CancellationToken.None);
        var first = await firstResponse.Content.ReadFromJsonAsync<PostPurchaseOrderReceiptResponse>(CancellationToken.None);
        var second = await secondResponse.Content.ReadFromJsonAsync<PostPurchaseOrderReceiptResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.False(first.AlreadyPosted);
        Assert.True(second.AlreadyPosted);
        Assert.Equal(first.Receipt.ReceiptId, second.Receipt.ReceiptId);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));
        Assert.Equal(1, await ReceiptCountAsync(seed.OrderId));
    }

    [Fact]
    public async Task Reusing_a_client_request_id_with_different_content_returns_409()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var body = await ConfirmedBodyAsync(client, seed, (0, 4m));
        using var firstResponse = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), body, CancellationToken.None);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            body with { Lines = [Line(seed, 0, 5m)] },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Client request id was already used.", problem?.Title);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Posting_after_another_receipt_changed_the_order_returns_409()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var first = await ConfirmedBodyAsync(client, seed, (0, 4m));
        var stale = await ConfirmedBodyAsync(client, seed, (0, 1m));
        using var firstResponse = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), first, CancellationToken.None);

        using var response = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), stale, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order was modified concurrently.", problem?.Title);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Posting_a_preview_made_stale_by_a_stock_change_returns_409()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 5m, 10m);
        var body = await ConfirmedBodyAsync(client, seed, (0, 4m));
        await UseStockAsync(seed.ItemIds[0], 1m);

        using var response = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId), body, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order receipt preview is out of date.", problem?.Title);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));
        Assert.Equal(0, await ReceiptCountAsync(seed.OrderId));
    }

    [Fact]
    public async Task Over_receipt_returns_409_without_moving_stock()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 2m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            await ConfirmedBodyAsync(client, seed, (0, 10.001m)),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order receipt has posting blockers.", problem?.Title);
        Assert.Equal(2m, await StoredQuantityAsync(seed.ItemIds[0]));
        Assert.Equal(PurchaseOrderStatus.Ordered, await StoredStatusAsync(seed.OrderId));
    }


    [Fact]
    public async Task Invalid_receipts_return_400_with_field_errors()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            Body(seed, (0, 1m), (0, 2m)) with
            {
                ClientRequestId = null,
                ReceivedUtc = DateTime.UtcNow.AddHours(1),
                DeliveryReference = new string('R', 101),
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            ["clientRequestId", "deliveryReference", "lines[1].purchaseOrderLineId", "receivedUtc"],
            problem.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task A_line_from_another_order_returns_400()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var other = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            Body(seed) with { Lines = [Line(other, 0, 1m)] },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["lines[0].purchaseOrderLineId"], problem?.Errors.Keys);
        Assert.Equal(0m, await StoredQuantityAsync(other.ItemIds[0]));
    }

    [Fact]
    public async Task Posting_to_an_unknown_order_returns_404()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(Guid.NewGuid()),
            Body(seed, (0, 1m)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Receive_with_a_client_request_id_is_idempotent()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.PartiallyReceived, quantityOnHand: 0m, 10m);
        var uri = new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive", UriKind.Relative);
        using var previewResponse = await client.GetAsync(new Uri($"{uri}/preview", UriKind.Relative), CancellationToken.None);
        var preview = await previewResponse.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);
        var body = new
        {
            clientRequestId = Guid.NewGuid(),
            expectedConcurrencyToken = preview!.ConcurrencyToken,
            previewToken = preview.PreviewToken,
            deliveryReference = "DN-final",
        };

        using var firstResponse = await client.PostAsJsonAsync(uri, body, CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(uri, body, CancellationToken.None);
        var first = await firstResponse.Content.ReadFromJsonAsync<ChangePurchaseOrderStatusResponse>(CancellationToken.None);
        var second = await secondResponse.Content.ReadFromJsonAsync<ChangePurchaseOrderStatusResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(first?.Receipt);
        Assert.NotNull(second?.Receipt);
        Assert.False(first.AlreadyPosted);
        Assert.True(second.AlreadyPosted);
        Assert.Equal(first.Receipt.ReceiptId, second.Receipt.ReceiptId);
        Assert.Equal("DN-final", first.Receipt.DeliveryReference);
        Assert.Equal(5m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Receive_rejects_an_invalid_optional_body_with_400()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive", UriKind.Relative),
            new { receivedUtc = DateTime.UtcNow.AddDays(1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task History_is_paged_newest_first_and_single_receipts_can_be_read()
    {
        using var manager = await CreateManagerClientAsync();
        using var reader = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var receiptIds = new List<Guid>();

        for (var delivery = 0; delivery < 3; delivery++)
        {
            using var postResponse = await manager.PostAsJsonAsync(
                ReceiptsUri(seed.OrderId),
                await ConfirmedBodyAsync(manager, seed, (0, 1m)) with
                {
                    ReceivedUtc = DateTime.UtcNow.AddHours(-10 + delivery),
                },
                CancellationToken.None);
            var posted = await postResponse.Content.ReadFromJsonAsync<PostPurchaseOrderReceiptResponse>(CancellationToken.None);
            receiptIds.Add(posted!.Receipt.ReceiptId);
        }

        using var pageResponse = await reader.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receipts?pageNumber=1&pageSize=2", UriKind.Relative),
            CancellationToken.None);
        var page = await pageResponse.Content.ReadFromJsonAsync<GetPurchaseOrderReceiptsResponse>(CancellationToken.None);
        using var singleResponse = await reader.GetAsync(ReceiptsUri(seed.OrderId, $"/{receiptIds[0]}"), CancellationToken.None);
        var single = await singleResponse.Content.ReadFromJsonAsync<PurchaseOrderReceiptDetails>(CancellationToken.None);
        using var missingResponse = await reader.GetAsync(ReceiptsUri(seed.OrderId, $"/{Guid.NewGuid()}"), CancellationToken.None);
        using var invalidPageResponse = await reader.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receipts?pageSize=101", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal([receiptIds[2], receiptIds[1]], page.Items.Select(item => item.ReceiptId));
        Assert.Equal(HttpStatusCode.OK, singleResponse.StatusCode);
        Assert.Equal(receiptIds[0], single?.ReceiptId);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);
    }

    [Fact]
    public async Task Readers_can_preview_and_read_history_but_not_post()
    {
        using var reader = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var preview = await reader.PostAsJsonAsync(ReceiptsUri(seed.OrderId, "/preview"), Body(seed, (0, 1m)), CancellationToken.None);
        using var history = await reader.GetAsync(ReceiptsUri(seed.OrderId), CancellationToken.None);
        using var post = await reader.PostAsJsonAsync(ReceiptsUri(seed.OrderId), Body(seed, (0, 1m)), CancellationToken.None);
        using var receive = await reader.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, receive.StatusCode);
        Assert.Equal(0m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Anonymous_and_unrelated_callers_are_rejected()
    {
        using var anonymous = _application.CreateClient();
        using var unrelated = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var anonymousPost = await anonymous.PostAsJsonAsync(ReceiptsUri(seed.OrderId), Body(seed, (0, 1m)), CancellationToken.None);
        using var anonymousHistory = await anonymous.GetAsync(ReceiptsUri(seed.OrderId), CancellationToken.None);
        using var unrelatedPreview = await unrelated.PostAsJsonAsync(ReceiptsUri(seed.OrderId, "/preview"), Body(seed, (0, 1m)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHistory.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedPreview.StatusCode);
    }

    [Fact]
    public async Task Partially_received_orders_are_filtered_and_counted_as_overdue()
    {
        using var client = await CreateReaderClientAsync();
        var partial = await SeedOrderAsync(
            PurchaseOrderStatus.PartiallyReceived,
            quantityOnHand: 0m,
            expectedDelivery: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3),
            10m);
        await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var byStatus = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?status=PartiallyReceived", UriKind.Relative),
            CancellationToken.None);
        using var overdue = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?overdue=true", UriKind.Relative),
            CancellationToken.None);
        var statusResult = await byStatus.Content.ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);
        var overdueResult = await overdue.Content.ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);

        var item = Assert.Single(statusResult!.Items);

        Assert.Equal(partial.OrderId, item.PurchaseOrderId);
        Assert.Equal(5m, item.QuantityReceived);
        Assert.Equal(5m, item.QuantityOutstanding);
        Assert.True(item.IsOverdue);
        Assert.Equal(partial.OrderId, Assert.Single(overdueResult!.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task Supplier_returns_from_a_partially_received_order_are_capped_at_the_received_quantity()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.PartiallyReceived, quantityOnHand: 5m, 10m);

        using var tooMuch = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/supplier-returns", UriKind.Relative),
            ReturnBody(seed, 5.001m),
            CancellationToken.None);
        using var allowed = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/supplier-returns", UriKind.Relative),
            ReturnBody(seed, 5m),
            CancellationToken.None);
        var details = await allowed.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), details?.PurchaseOrderStatus);
        Assert.Equal(5m, Assert.Single(details!.Lines).ReceivedQuantity);
    }

    [Fact]
    public async Task A_legacy_received_order_is_shown_as_received_cannot_be_received_again_and_stays_returnable()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 7m, 10m);
        await SeedLegacyReceivedAsync(seed.OrderId);

        var details = await GetDetailsAsync(client, seed.OrderId);
        using var receive = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);
        using var supplierReturn = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/supplier-returns", UriKind.Relative),
            ReturnBody(seed, 7m),
            CancellationToken.None);

        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.Received), details.Status);
        Assert.Equal(10m, line.LegacyReceivedQuantity);
        Assert.Equal(0m, line.OutstandingQuantity);
        Assert.Equal(0, details.ReceiptCount);
        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
        Assert.Equal(HttpStatusCode.Created, supplierReturn.StatusCode);
        Assert.Equal(7m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    private sealed record OrderSeed(Guid OrderId, Guid ConcurrencyToken, IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> ItemIds);

    private sealed record ReceiptBody
    {
        public Guid? ClientRequestId { get; init; }

        public Guid? ExpectedConcurrencyToken { get; init; }

        public string? PreviewToken { get; init; }

        public DateTime? ReceivedUtc { get; init; }

        public string? DeliveryReference { get; init; }

        public string? Notes { get; init; }

        public IReadOnlyList<ReceiptLineBody> Lines { get; init; } = [];
    }

    private sealed record ReceiptLineBody(Guid PurchaseOrderLineId, decimal Quantity);

    private static ReceiptBody Body(OrderSeed seed, params (int LineIndex, decimal Quantity)[] lines)
    {
        return new ReceiptBody
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = seed.ConcurrencyToken,
            PreviewToken = "not-confirmed",
            ReceivedUtc = ReceivedUtc,
            Lines = [.. lines.Select(line => Line(seed, line.LineIndex, line.Quantity))],
        };
    }

    // Previews the receipt and returns the body a user would send to confirm exactly that preview.
    private static async Task<ReceiptBody> ConfirmedBodyAsync(
        HttpClient client,
        OrderSeed seed,
        params (int LineIndex, decimal Quantity)[] lines)
    {
        var body = Body(seed, lines);
        using var response = await client.PostAsJsonAsync(ReceiptsUri(seed.OrderId, "/preview"), body, CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return body with
        {
            ExpectedConcurrencyToken = preview!.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
        };
    }

    private static ReceiptLineBody Line(OrderSeed seed, int lineIndex, decimal quantity)
    {
        return new ReceiptLineBody(seed.LineIds[lineIndex], quantity);
    }

    private static object ReturnBody(OrderSeed seed, decimal quantity)
    {
        return new
        {
            purchaseOrderId = seed.OrderId,
            lines = new[] { new { purchaseOrderLineId = seed.LineIds[0], quantity, reason = "Damaged" } },
        };
    }

    private static Uri ReceiptsUri(Guid purchaseOrderId, string suffix = "")
    {
        return new Uri($"/api/maintenance/purchase-orders/{purchaseOrderId}/receipts{suffix}", UriKind.Relative);
    }

    private Task<HttpClient> CreateManagerClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read,
            Permissions.Maintenance.Manage);
    }

    private Task<HttpClient> CreateReaderClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
    }

    private static async Task<GetPurchaseOrderDetailsResponse> GetDetailsAsync(HttpClient client, Guid purchaseOrderId)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{purchaseOrderId}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<GetPurchaseOrderDetailsResponse>(CancellationToken.None))!;
    }

    private Task<OrderSeed> SeedOrderAsync(PurchaseOrderStatus status, decimal quantityOnHand, params decimal[] ordered)
    {
        return SeedOrderAsync(status, quantityOnHand, expectedDelivery: null, ordered);
    }

    private async Task<OrderSeed> SeedOrderAsync(
        PurchaseOrderStatus status,
        decimal quantityOnHand,
        DateOnly? expectedDelivery,
        params decimal[] ordered)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var items = ordered
            .Select(_ => InventoryItem.Create(
                Guid.NewGuid(),
                unit.Id,
                supplier.Id,
                "Cooling pump impeller",
                $"IMP-{Guid.NewGuid():N}",
                quantityOnHand,
                minimumQuantity: 1m,
                unitCost: 150m))
            .ToList();
        var order = PurchaseOrder.Create(supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10), expectedDelivery);
        var lines = items
            .Select((item, index) => PurchaseOrderLine.Create(order.Id, item.Id, ordered[index], 150m))
            .ToList();

        PurchaseOrderSeedData.ApplyStatus(order, status, lines);

        await dbContext.AddRangeAsync(supplier, unit, order);
        await dbContext.AddRangeAsync(items);
        await dbContext.AddRangeAsync(lines);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new OrderSeed(
            order.Id,
            order.ConcurrencyToken,
            lines.Select(line => line.Id).ToList(),
            items.Select(item => item.Id).ToList());
    }

    // Mirrors the migration's legacy opening balance: status and quantities change, stock does not.
    private async Task SeedLegacyReceivedAsync(Guid purchaseOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var order = await dbContext.Set<PurchaseOrder>().SingleAsync(candidate => candidate.Id == purchaseOrderId);
        dbContext.Entry(order).Property(candidate => candidate.Status).CurrentValue = PurchaseOrderStatus.Received;

        foreach (var line in await dbContext.Set<PurchaseOrderLine>()
            .Where(candidate => candidate.PurchaseOrderId == purchaseOrderId)
            .ToListAsync())
        {
            dbContext.Entry(line).Property(candidate => candidate.LegacyReceivedQuantity).CurrentValue = line.QuantityOrdered;
            dbContext.Entry(line).Property(candidate => candidate.ReceivedQuantity).CurrentValue = line.QuantityOrdered;
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    // Simulates part usage landing between a preview and its confirmation.
    private async Task UseStockAsync(Guid inventoryItemId, decimal quantity)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var item = await dbContext.Set<InventoryItem>().SingleAsync(candidate => candidate.Id == inventoryItemId);
        item.ReduceStock(quantity);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<decimal> StoredQuantityAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
    }

    private async Task<PurchaseOrderStatus> StoredStatusAsync(Guid purchaseOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == purchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync();
    }

    private async Task<int> ReceiptCountAsync(Guid purchaseOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .CountAsync(receipt => receipt.PurchaseOrderId == purchaseOrderId);
    }
}
