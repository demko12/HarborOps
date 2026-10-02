using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class PurchaseOrderClosureEndpointTests : IntegrationTestBase
{
    private const string Reason = "Supplier discontinued the part";

    private readonly HarborOpsApiFactory _application;

    public PurchaseOrderClosureEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    // The maintenance module runs on the system clock, so receipt times are taken relative to real time.
    private static DateTime ReceivedUtc => DateTime.UtcNow.AddHours(-1);

    [Fact]
    public async Task Preview_shows_received_and_undelivered_amounts_to_a_reader_without_changing_anything()
    {
        using var manager = await CreateManagerClientAsync();
        using var reader = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(manager, seed, (0, 4m));

        using var response = await reader.GetAsync(CloseUri(seed.OrderId, "/preview"), CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderClosurePreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.True(preview.CanClose);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), preview.ResultingOrderStatus);
        Assert.Equal(4m, preview.TotalQuantityReceived);
        Assert.Equal(6m, preview.TotalQuantityToClose);

        var line = Assert.Single(preview.Lines);

        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(0m, line.ReturnedQuantity);
        Assert.Equal(6m, line.QuantityToClose);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, await StoredStatusAsync(seed.OrderId));
        Assert.Equal(0, await ClosureCountAsync());
    }

    [Fact]
    public async Task Closing_ten_ordered_with_four_received_closes_six_and_leaves_stock_unchanged()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));

        using var response = await client.PostAsJsonAsync(
            CloseUri(seed.OrderId),
            await ConfirmedBodyAsync(client, seed.OrderId),
            CancellationToken.None);
        var closed = await response.Content.ReadFromJsonAsync<ClosePurchaseOrderResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(closed);
        Assert.False(closed.AlreadyClosed);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), closed.OrderStatus);
        Assert.Equal([PurchaseOrderActions.CreateReturn], closed.AllowedActions);
        Assert.Equal(Reason, closed.Closure.Reason);
        Assert.NotEqual(Guid.Empty, closed.Closure.ClosedByUserId);
        Assert.Equal(6m, Assert.Single(closed.Closure.Lines).ClosedQuantity);
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));

        var details = await GetDetailsAsync(client, seed.OrderId);
        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), details.Status);
        Assert.Equal(10m, details.TotalQuantityOrdered);
        Assert.Equal(4m, details.TotalQuantityReceived);
        Assert.Equal(6m, details.TotalQuantityClosed);
        Assert.Equal(0m, details.TotalQuantityOutstanding);
        Assert.Equal(6m, line.ClosedQuantity);
        Assert.Equal(4m, line.ReturnableQuantity);
        Assert.Equal(1, details.ReceiptCount);
        Assert.Empty(details.AllowedTransitions);
        Assert.Equal([PurchaseOrderActions.CreateReturn], details.AllowedActions);

        using var historyResponse = await client.GetAsync(ClosureUri(seed.OrderId), CancellationToken.None);
        var history = await historyResponse.Content.ReadFromJsonAsync<PurchaseOrderClosureDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(closed.Closure.ClosureId, history?.ClosureId);
        Assert.Equal(closed.Closure.ClosedByUserId, history?.ClosedByUserId);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), history?.OrderStatusBefore);
    }

    [Fact]
    public async Task Returning_one_received_unit_after_closing_never_reopens_the_closed_quantity()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));
        await CloseAsync(client, seed.OrderId);

        using var tooMuch = await client.PostAsJsonAsync(ReturnsUri(), ReturnBody(seed, 4.001m), CancellationToken.None);
        using var created = await client.PostAsJsonAsync(ReturnsUri(), ReturnBody(seed, 1m), CancellationToken.None);
        var draft = await created.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None);
        using var previewResponse = await client.GetAsync(ReturnsUri($"/{draft!.SupplierReturnId}/preview"), CancellationToken.None);
        var returnPreview = await previewResponse.Content.ReadFromJsonAsync<SupplierReturnPostingPreview>(CancellationToken.None);
        using var posted = await client.PostAsJsonAsync(
            ReturnsUri($"/{draft.SupplierReturnId}/post"),
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = returnPreview!.ConcurrencyToken,
                PreviewToken = returnPreview.PreviewToken,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), draft.PurchaseOrderStatus);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var details = await GetDetailsAsync(client, seed.OrderId);
        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), details.Status);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
        Assert.Equal(0m, line.OutstandingQuantity);
        Assert.Equal(3m, line.ReturnableQuantity);
        Assert.Equal(3m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Repeating_an_identical_close_returns_the_original_closure_and_different_content_returns_409()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));
        var body = await ConfirmedBodyAsync(client, seed.OrderId);

        using var firstResponse = await client.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        using var changedResponse = await client.PostAsJsonAsync(
            CloseUri(seed.OrderId),
            body with { Reason = "Supplier went out of business" },
            CancellationToken.None);
        var first = await firstResponse.Content.ReadFromJsonAsync<ClosePurchaseOrderResponse>(CancellationToken.None);
        var second = await secondResponse.Content.ReadFromJsonAsync<ClosePurchaseOrderResponse>(CancellationToken.None);
        var problem = await changedResponse.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.False(first!.AlreadyClosed);
        Assert.True(second!.AlreadyClosed);
        Assert.Equal(first.Closure.ClosureId, second.Closure.ClosureId);
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);
        Assert.Equal("Client request id was already used.", problem?.Title);
        Assert.Equal(1, await ClosureCountAsync());
    }

    [Fact]
    public async Task Closing_after_a_receipt_changed_the_order_returns_409_with_the_refreshed_preview()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));
        var body = await ConfirmedBodyAsync(client, seed.OrderId);
        await ReceiveAsync(client, seed, (0, 1m));

        using var response = await client.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        var (problem, refreshed) = await ReadConflictAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order was modified concurrently.", problem.GetProperty("title").GetString());
        Assert.Equal(5m, refreshed.TotalQuantityReceived);
        Assert.Equal(5m, refreshed.TotalQuantityToClose);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, await StoredStatusAsync(seed.OrderId));

        // Confirming the refreshed amounts closes exactly what is still outstanding.
        using var retry = await client.PostAsJsonAsync(
            CloseUri(seed.OrderId),
            BodyFor(refreshed),
            CancellationToken.None);
        var closed = await retry.Content.ReadFromJsonAsync<ClosePurchaseOrderResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(5m, closed?.Closure.TotalQuantityClosed);
    }

    [Fact]
    public async Task Closing_a_preview_made_stale_by_a_return_returns_409_with_the_refreshed_preview()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));
        var body = await ConfirmedBodyAsync(client, seed.OrderId);
        await PostReturnAsync(client, seed, 1m);

        using var response = await client.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        var (problem, refreshed) = await ReadConflictAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order closure preview is out of date.", problem.GetProperty("title").GetString());
        Assert.Equal(1m, Assert.Single(refreshed.Lines).ReturnedQuantity);
        Assert.Equal(0, await ClosureCountAsync());
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Orders_that_cannot_be_closed_return_409_with_blockers_in_the_preview(PurchaseOrderStatus status)
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(status, quantityOnHand: 0m, 10m);

        using var previewResponse = await client.GetAsync(CloseUri(seed.OrderId, "/preview"), CancellationToken.None);
        var preview = await previewResponse.Content.ReadFromJsonAsync<PurchaseOrderClosurePreview>(CancellationToken.None);

        Assert.NotNull(preview);

        using var response = await client.PostAsJsonAsync(CloseUri(seed.OrderId), BodyFor(preview), CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.False(preview.CanClose);
        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(preview.Blockers).Code);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Purchase order cannot be closed.", problem?.Title);
        Assert.Equal(status, await StoredStatusAsync(seed.OrderId));
        Assert.Equal(0, await ClosureCountAsync());
    }

    [Fact]
    public async Task Invalid_close_requests_return_400_with_field_errors()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));

        using var response = await client.PostAsJsonAsync(
            CloseUri(seed.OrderId),
            new CloseBody { Reason = "Late" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            ["clientRequestId", "expectedConcurrencyToken", "previewToken", "reason"],
            problem.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, await StoredStatusAsync(seed.OrderId));
    }

    [Fact]
    public async Task Unknown_orders_and_orders_without_a_closure_return_404()
    {
        using var client = await CreateManagerClientAsync();
        var unknownOrderId = Guid.NewGuid();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);

        using var preview = await client.GetAsync(CloseUri(unknownOrderId, "/preview"), CancellationToken.None);
        using var close = await client.PostAsJsonAsync(
            CloseUri(unknownOrderId),
            new CloseBody
            {
                ClientRequestId = Guid.NewGuid(),
                ExpectedConcurrencyToken = Guid.NewGuid(),
                PreviewToken = "ABC",
                Reason = Reason,
            },
            CancellationToken.None);
        using var unknownClosure = await client.GetAsync(ClosureUri(unknownOrderId), CancellationToken.None);
        using var missingClosure = await client.GetAsync(ClosureUri(seed.OrderId), CancellationToken.None);
        var missingProblem = await missingClosure.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownClosure.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingClosure.StatusCode);
        Assert.Equal("Purchase order closure not found.", missingProblem?.Title);
    }

    [Fact]
    public async Task Every_receive_entry_point_and_cancellation_reject_a_closed_order()
    {
        using var client = await CreateManagerClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(client, seed, (0, 4m));
        await CloseAsync(client, seed.OrderId);

        using var receiptPreview = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId, "/preview"),
            new { receivedUtc = ReceivedUtc, lines = new[] { new { purchaseOrderLineId = seed.LineIds[0], quantity = 1m } } },
            CancellationToken.None);
        var preview = await receiptPreview.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);
        using var receipt = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            new
            {
                clientRequestId = Guid.NewGuid(),
                expectedConcurrencyToken = preview!.ConcurrencyToken,
                previewToken = preview.PreviewToken,
                receivedUtc = ReceivedUtc,
                lines = new[] { new { purchaseOrderLineId = seed.LineIds[0], quantity = 1m } },
            },
            CancellationToken.None);
        using var receiveAll = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);
        using var cancel = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{seed.OrderId}/cancel", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.False(preview.CanPost);
        Assert.Contains(preview.Blockers, blocker => blocker.Code == PurchaseOrderReceiptBlockerCodes.OrderNotReceivable);
        Assert.Equal(HttpStatusCode.Conflict, receipt.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, receiveAll.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal(PurchaseOrderStatus.ClosedShort, await StoredStatusAsync(seed.OrderId));
        Assert.Equal(4m, await StoredQuantityAsync(seed.ItemIds[0]));
    }

    [Fact]
    public async Task Readers_can_preview_and_read_the_closure_but_not_close()
    {
        using var manager = await CreateManagerClientAsync();
        using var reader = await CreateReaderClientAsync();
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        await ReceiveAsync(manager, seed, (0, 4m));
        var body = await ConfirmedBodyAsync(reader, seed.OrderId);

        using var close = await reader.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        await CloseAsync(manager, seed.OrderId);
        using var closure = await reader.GetAsync(ClosureUri(seed.OrderId), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closure.StatusCode);
    }

    [Fact]
    public async Task Anonymous_and_unrelated_callers_are_rejected()
    {
        using var anonymous = _application.CreateClient();
        using var unrelated = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var seed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, 10m);
        var body = new CloseBody
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = seed.ConcurrencyToken,
            PreviewToken = "ABC",
            Reason = Reason,
        };

        using var anonymousClose = await anonymous.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);
        using var anonymousPreview = await anonymous.GetAsync(CloseUri(seed.OrderId, "/preview"), CancellationToken.None);
        using var unrelatedPreview = await unrelated.GetAsync(CloseUri(seed.OrderId, "/preview"), CancellationToken.None);
        using var unrelatedClosure = await unrelated.GetAsync(ClosureUri(seed.OrderId), CancellationToken.None);
        using var unrelatedClose = await unrelated.PostAsJsonAsync(CloseUri(seed.OrderId), body, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousClose.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedClosure.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedClose.StatusCode);
        Assert.Equal(PurchaseOrderStatus.Ordered, await StoredStatusAsync(seed.OrderId));
    }

    [Fact]
    public async Task Closed_short_orders_are_filtered_and_never_counted_as_overdue_or_incoming()
    {
        using var client = await CreateManagerClientAsync();
        var overdueDelivery = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var closedSeed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, overdueDelivery, 10m);
        var openSeed = await SeedOrderAsync(PurchaseOrderStatus.Ordered, quantityOnHand: 0m, overdueDelivery, 10m);
        await ReceiveAsync(client, closedSeed, (0, 4m));
        await CloseAsync(client, closedSeed.OrderId);

        using var byStatus = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?status=ClosedShort", UriKind.Relative),
            CancellationToken.None);
        using var overdue = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?overdue=true", UriKind.Relative),
            CancellationToken.None);
        using var replenishment = await client.GetAsync(
            new Uri("/api/maintenance/inventory/replenishment", UriKind.Relative),
            CancellationToken.None);
        var statusResult = await byStatus.Content.ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);
        var overdueResult = await overdue.Content.ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);
        var replenishmentResult = await replenishment.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(CancellationToken.None);

        var item = Assert.Single(statusResult!.Items);

        Assert.Equal(closedSeed.OrderId, item.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), item.Status);
        Assert.Equal(4m, item.QuantityReceived);
        Assert.Equal(6m, item.QuantityClosed);
        Assert.Equal(0m, item.QuantityOutstanding);
        Assert.False(item.IsOverdue);
        Assert.Equal(openSeed.OrderId, Assert.Single(overdueResult!.Items).PurchaseOrderId);
        Assert.Equal(HttpStatusCode.OK, replenishment.StatusCode);
        Assert.Equal(
            0m,
            replenishmentResult!.Items.Single(row => row.InventoryItemId == closedSeed.ItemIds[0]).OpenPurchaseOrderQuantity);
    }

    private sealed record OrderSeed(Guid OrderId, Guid ConcurrencyToken, IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> ItemIds);

    private sealed record CloseBody
    {
        public Guid? ClientRequestId { get; init; }

        public Guid? ExpectedConcurrencyToken { get; init; }

        public string? PreviewToken { get; init; }

        public string? Reason { get; init; }
    }

    private static CloseBody BodyFor(PurchaseOrderClosurePreview preview)
    {
        return new CloseBody
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            Reason = Reason,
        };
    }

    // Previews the closure and returns the body a user would send to confirm exactly that preview.
    private static async Task<CloseBody> ConfirmedBodyAsync(HttpClient client, Guid purchaseOrderId)
    {
        using var response = await client.GetAsync(CloseUri(purchaseOrderId, "/preview"), CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PurchaseOrderClosurePreview>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return BodyFor(preview!);
    }

    private static async Task<ClosePurchaseOrderResponse> CloseAsync(HttpClient client, Guid purchaseOrderId)
    {
        using var response = await client.PostAsJsonAsync(
            CloseUri(purchaseOrderId),
            await ConfirmedBodyAsync(client, purchaseOrderId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ClosePurchaseOrderResponse>(CancellationToken.None))!;
    }

    // Posts a real receipt through the receipt endpoints, so the order has receipt history to close against.
    private static async Task ReceiveAsync(HttpClient client, OrderSeed seed, params (int LineIndex, decimal Quantity)[] lines)
    {
        var receivedUtc = ReceivedUtc;
        var entries = lines
            .Select(line => new { purchaseOrderLineId = seed.LineIds[line.LineIndex], quantity = line.Quantity })
            .ToArray();

        using var previewResponse = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId, "/preview"),
            new { receivedUtc, lines = entries },
            CancellationToken.None);
        var preview = await previewResponse.Content.ReadFromJsonAsync<PurchaseOrderReceiptPostingPreview>(CancellationToken.None);
        using var response = await client.PostAsJsonAsync(
            ReceiptsUri(seed.OrderId),
            new
            {
                clientRequestId = Guid.NewGuid(),
                expectedConcurrencyToken = preview!.ConcurrencyToken,
                previewToken = preview.PreviewToken,
                receivedUtc,
                lines = entries,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task PostReturnAsync(HttpClient client, OrderSeed seed, decimal quantity)
    {
        using var created = await client.PostAsJsonAsync(ReturnsUri(), ReturnBody(seed, quantity), CancellationToken.None);
        var draft = await created.Content.ReadFromJsonAsync<SupplierReturnDetails>(CancellationToken.None);
        using var previewResponse = await client.GetAsync(ReturnsUri($"/{draft!.SupplierReturnId}/preview"), CancellationToken.None);
        var preview = await previewResponse.Content.ReadFromJsonAsync<SupplierReturnPostingPreview>(CancellationToken.None);
        using var posted = await client.PostAsJsonAsync(
            ReturnsUri($"/{draft.SupplierReturnId}/post"),
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = preview!.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
    }

    // A recoverable conflict carries the refreshed preview as the "preview" problem details extension.
    private static async Task<(JsonElement Problem, PurchaseOrderClosurePreview Preview)> ReadConflictAsync(
        HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var preview = problem.GetProperty("preview").Deserialize<PurchaseOrderClosurePreview>(JsonSerializerOptions.Web);

        Assert.NotNull(preview);

        return (problem, preview);
    }

    private static object ReturnBody(OrderSeed seed, decimal quantity)
    {
        return new
        {
            purchaseOrderId = seed.OrderId,
            lines = new[] { new { purchaseOrderLineId = seed.LineIds[0], quantity, reason = "Damaged" } },
        };
    }

    private static Uri CloseUri(Guid purchaseOrderId, string suffix = "")
    {
        return new Uri($"/api/maintenance/purchase-orders/{purchaseOrderId}/close{suffix}", UriKind.Relative);
    }

    private static Uri ClosureUri(Guid purchaseOrderId)
    {
        return new Uri($"/api/maintenance/purchase-orders/{purchaseOrderId}/closure", UriKind.Relative);
    }

    private static Uri ReceiptsUri(Guid purchaseOrderId, string suffix = "")
    {
        return new Uri($"/api/maintenance/purchase-orders/{purchaseOrderId}/receipts{suffix}", UriKind.Relative);
    }

    private static Uri ReturnsUri(string suffix = "")
    {
        return new Uri($"/api/maintenance/supplier-returns{suffix}", UriKind.Relative);
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
        return SeedOrderAsync(status, quantityOnHand, null, ordered);
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
                minimumQuantity: 20m,
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

    private async Task<int> ClosureCountAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<PurchaseOrderClosure>().AsNoTracking().CountAsync();
    }
}
