using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderReceiptTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostPurchaseOrderReceiptServiceTests
{
    [Fact]
    public async Task PostAsync_receives_part_of_a_line_with_a_traceable_movement()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 3m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var tokenBefore = order.ConcurrencyToken;
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));
        var request = Request(preview, (lines[0], 4m)) with { DeliveryReference = " DN-4471 ", Notes = " Box 1 " };

        var response = await PostAsync(dbContext, order.Id, request);

        var line = Assert.Single(response.Receipt.Lines);
        var movement = Assert.Single(response.Receipt.Movements);

        Assert.False(response.AlreadyPosted);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.OrderStatus);
        Assert.NotEqual(tokenBefore, response.ConcurrencyToken);
        Assert.Equal(
            [
                PurchaseOrderActions.ReceivePartial,
                PurchaseOrderActions.ReceiveRemaining,
                PurchaseOrderActions.CloseRemaining,
                PurchaseOrderActions.CreateReturn,
            ],
            response.AllowedActions);
        Assert.Equal(request.ClientRequestId, response.Receipt.ClientRequestId);
        Assert.Equal(ReceivedUtc, response.Receipt.ReceivedUtc);
        Assert.Equal("DN-4471", response.Receipt.DeliveryReference);
        Assert.Equal("Box 1", response.Receipt.Notes);
        Assert.Equal(ActorUserId, response.Receipt.RecordedByUserId);
        Assert.Equal(UtcNow, response.Receipt.RecordedUtc);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), response.Receipt.OrderStatusBefore);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.Receipt.OrderStatusAfter);
        Assert.Equal(4m, response.Receipt.TotalQuantity);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal("Main engine filter", line.Name);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(4m, line.Quantity);
        Assert.Equal(4m, line.LineReceivedQuantityAfter);
        Assert.Equal(6m, line.OutstandingQuantityAfter);
        Assert.Equal(item.Id, movement.InventoryItemId);
        Assert.Equal(response.Receipt.ReceiptId, movement.PurchaseOrderReceiptId);
        Assert.Equal(line.ReceiptLineId, movement.PurchaseOrderReceiptLineId);
        Assert.Equal(3m, movement.QuantityBefore);
        Assert.Equal(7m, movement.QuantityAfter);
        Assert.Equal(4m, movement.QuantityDelta);
        Assert.Equal("Received from purchase order, delivery DN-4471", movement.Reason);
        Assert.Equal(ActorUserId, movement.ActorUserId);
        Assert.Equal(UtcNow, movement.MovedUtc);
        Assert.Equal(7m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_closes_the_order_once_the_remaining_quantity_arrives()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));
        var second = await ReceiveAsync(dbContext, order.Id, (lines[0], 6m));

        Assert.Equal(nameof(PurchaseOrderStatus.Received), second.OrderStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), second.Receipt.OrderStatusBefore);
        Assert.Equal([PurchaseOrderActions.CreateReturn], second.AllowedActions);
        Assert.Equal(0m, Assert.Single(second.Receipt.Lines).OutstandingQuantityAfter);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(2, await ReceiptCountAsync(dbContext, order.Id));
        Assert.Equal(2, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task PostAsync_commits_the_receipt_movements_line_totals_stock_and_status_together()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 1m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));

        var response = await ReceiveAsync(dbContext, order.Id, (lines[0], 2.5m), (lines[1], 2m));

        await using var verification = CreateDbContext(databaseName);
        var receipt = await verification.Set<PurchaseOrderReceipt>().SingleAsync();
        var receiptLines = await verification.Set<PurchaseOrderReceiptLine>().ToListAsync();
        var movements = await verification.Set<InventoryAdjustment>().ToListAsync();
        var storedLines = await verification.Set<PurchaseOrderLine>().ToDictionaryAsync(line => line.Id);
        var storedOrder = await verification.Set<PurchaseOrder>().SingleAsync();

        Assert.Equal(response.Receipt.ReceiptId, receipt.Id);
        Assert.Equal(PurchaseOrderReceipt.RequestHashLength, receipt.RequestHash.Length);
        Assert.Equal(2, receiptLines.Count);
        Assert.All(receiptLines, line => Assert.Equal(receipt.Id, line.PurchaseOrderReceiptId));
        Assert.Equal(2, movements.Count);
        Assert.All(movements, movement => Assert.Equal(receipt.Id, movement.PurchaseOrderReceiptId));
        Assert.Equal(
            receiptLines.Select(line => line.Id).Order(),
            movements.Select(movement => movement.PurchaseOrderReceiptLineId!.Value).Order());
        Assert.Equal(2.5m, storedLines[lines[0].Id].ReceivedQuantity);
        Assert.Equal(2m, storedLines[lines[1].Id].ReceivedQuantity);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, storedOrder.Status);
        Assert.Equal(response.ConcurrencyToken, storedOrder.ConcurrencyToken);
        Assert.Equal(3.5m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(2m, await StoredQuantityAsync(verification, other.Id));
    }

    [Fact]
    public async Task PostAsync_returns_the_original_result_for_an_identical_repeat()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var request = Request(await PreviewAsync(dbContext, order.Id, (lines[0], 4m)), (lines[0], 4m));
        var original = await PostAsync(dbContext, order.Id, request);

        // The repeat still carries the tokens of the preview it was confirmed from.
        var repeat = await PostAsync(dbContext, order.Id, request);

        Assert.True(repeat.AlreadyPosted);
        Assert.Equal(original.Receipt.ReceiptId, repeat.Receipt.ReceiptId);
        Assert.Equal(original.Receipt.Lines, repeat.Receipt.Lines);
        Assert.Equal(original.Receipt.Movements, repeat.Receipt.Movements);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await ReceiptCountAsync(dbContext, order.Id));
        Assert.Equal(1, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task PostAsync_treats_reordered_lines_and_padded_text_as_the_same_request()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 4m), (lines[1], 1m));
        var request = Request(preview, (lines[0], 4m), (lines[1], 1m)) with { DeliveryReference = "DN-1" };
        await PostAsync(dbContext, order.Id, request);

        var repeat = await PostAsync(
            dbContext,
            order.Id,
            request with { DeliveryReference = "  DN-1 ", Lines = [.. request.Lines!.Reverse()] });

        Assert.True(repeat.AlreadyPosted);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    public static TheoryData<string> ChangedContent => ["quantity", "line", "receivedUtc", "reference", "notes"];

    [Theory]
    [MemberData(nameof(ChangedContent))]
    public async Task PostAsync_rejects_a_reused_client_request_id_with_different_content(string change)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));
        var request = Request(await PreviewAsync(dbContext, order.Id, (lines[0], 4m)), (lines[0], 4m));
        await PostAsync(dbContext, order.Id, request);

        var changed = change switch
        {
            "quantity" => request with { Lines = [Entry(lines[0], 3m)] },
            "line" => request with { Lines = [Entry(lines[1], 1m)] },
            "receivedUtc" => request with { ReceivedUtc = ReceivedUtc.AddMinutes(-1) },
            "reference" => request with { DeliveryReference = "DN-2" },
            _ => request with { Notes = "Second pallet" },
        };

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptRequestConflictException>(
            () => PostAsync(dbContext, order.Id, changed));

        Assert.Equal(request.ClientRequestId, exception.ClientRequestId);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, other.Id));
        Assert.Equal(1, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_client_request_id_already_used_on_another_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var (otherOrder, otherLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var request = Request(await PreviewAsync(dbContext, order.Id, (lines[0], 4m)), (lines[0], 4m));
        await PostAsync(dbContext, order.Id, request);
        var otherPreview = await PreviewAsync(dbContext, otherOrder.Id, (otherLines[0], 4m));

        await Assert.ThrowsAsync<PurchaseOrderReceiptRequestConflictException>(
            () => PostAsync(
                dbContext,
                otherOrder.Id,
                Request(otherPreview, (otherLines[0], 4m)) with { ClientRequestId = request.ClientRequestId }));

        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(dbContext, otherOrder.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_stale_expected_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var stalePreview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m));
        await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(
            () => PostAsync(dbContext, order.Id, Request(stalePreview, (lines[0], 1m))));

        Assert.Equal(order.Id, exception.PurchaseOrderId);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_part_usage()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 5m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));
        item.ReduceStock(1m);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptStalePreviewException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 4m))));

        Assert.Equal(order.Id, exception.PurchaseOrderId);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_token_made_for_other_quantities()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));

        await Assert.ThrowsAsync<PurchaseOrderReceiptStalePreviewException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 5m))));

        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_cumulative_over_receipt_without_changing_anything()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var first = await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 6.001m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 6.001m))));

        var blocker = Assert.Single(exception.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(PurchaseOrderReceiptBlockerCodes.ExceedsOutstanding, blocker.Code);
        Assert.Equal(lines[0].Id, blocker.PurchaseOrderLineId);
        Assert.Contains("outstanding quantity 6.000", blocker.Message, StringComparison.Ordinal);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(4m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(4m, (await verification.Set<PurchaseOrderLine>().SingleAsync()).ReceivedQuantity);
        Assert.Equal(first.ConcurrencyToken, (await verification.Set<PurchaseOrder>().SingleAsync()).ConcurrencyToken);
        Assert.Equal(1, await ReceiptCountAsync(verification, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_line_that_was_already_fulfilled()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 2m), (other, 5m));
        await ReceiveAsync(dbContext, order.Id, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m), (lines[1], 1m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 1m), (lines[1], 1m))));

        Assert.Equal(lines[0].Id, Assert.Single(exception.Blockers).PurchaseOrderLineId);
        Assert.Equal(2m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, other.Id));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.Received)]
    public async Task PostAsync_rejects_orders_that_cannot_be_received(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 5m);
        var (order, lines) = await SeedOrderAsync(dbContext, status, (item, 10m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 1m))));

        Assert.Contains(exception.Blockers, blocker => blocker.Code == PurchaseOrderReceiptBlockerCodes.OrderNotReceivable);
        Assert.Equal(5m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_line_owned_by_another_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var (_, foreignLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<PostPurchaseOrderReceiptValidationException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 1m), (foreignLines[0], 1m))));

        Assert.Equal(["lines[1].purchaseOrderLineId"], exception.Errors.Keys);
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_an_invalid_request_before_touching_the_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<PostPurchaseOrderReceiptValidationException>(
            () => PostAsync(
                dbContext,
                order.Id,
                Request(preview, (lines[0], 1m)) with { ReceivedUtc = UtcNow.AddMinutes(1), PreviewToken = null }));

        Assert.Equal(["previewToken", "receivedUtc"], exception.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();
        var unknownOrderId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => PostAsync(
                dbContext,
                unknownOrderId,
                new PostPurchaseOrderReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(),
                    ExpectedConcurrencyToken = Guid.NewGuid(),
                    PreviewToken = "ABC",
                    ReceivedUtc = ReceivedUtc,
                    Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 1m }],
                }));

        Assert.Equal(unknownOrderId, exception.PurchaseOrderId);
    }

    [Fact]
    public async Task PostAsync_rejects_a_line_whose_inventory_item_is_missing()
    {
        await using var dbContext = CreateDbContext();
        var missingItem = InventoryItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Ghost", "PN-0", 0m, 0m, 1m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (missingItem, 10m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderItemsNotFoundException>(
            () => PostAsync(
                dbContext,
                order.Id,
                new PostPurchaseOrderReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(),
                    ExpectedConcurrencyToken = order.ConcurrencyToken,
                    PreviewToken = "ABC",
                    ReceivedUtc = ReceivedUtc,
                    Lines = [Entry(lines[0], 1m)],
                }));

        Assert.Equal([missingItem.Id], exception.InventoryItemIds);
        Assert.Equal(0, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task PostAsync_increases_a_part_repeated_across_lines_once_with_chained_movements()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 2m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 3m), (item, 5m));

        var response = await ReceiveAsync(dbContext, order.Id, (lines[0], 3m), (lines[1], 4m));

        var movements = response.Receipt.Movements;

        Assert.Equal(9m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(2, movements.Count);
        Assert.Equal(2m, movements[0].QuantityBefore);
        Assert.Equal(movements[0].QuantityAfter, movements[1].QuantityBefore);
        Assert.Equal(9m, movements[1].QuantityAfter);
        Assert.Equal(7m, response.Receipt.TotalQuantity);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.OrderStatus);
    }

    [Fact]
    public async Task PostAsync_replaces_the_stock_token_and_keeps_the_reservation_token()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 1m);
        var stockToken = item.StockConcurrencyToken;
        var reservationToken = item.ReservationConcurrencyToken;
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        await ReceiveAsync(dbContext, order.Id, (lines[0], 1m));

        await using var verification = CreateDbContext(databaseName);
        var stored = await verification.Set<InventoryItem>().SingleAsync();

        Assert.NotEqual(stockToken, stored.StockConcurrencyToken);
        Assert.Equal(reservationToken, stored.ReservationConcurrencyToken);
    }

    [Fact]
    public async Task ReceiveRemainingAsync_posts_only_the_outstanding_quantities_and_closes_the_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 1m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));
        await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));

        var response = await ReceiveRemainingAsync(dbContext, order.Id);

        Assert.False(response.AlreadyPosted);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), response.OrderStatus);
        Assert.Equal(8m, response.Receipt.TotalQuantity);
        Assert.Equal(6m, response.Receipt.Lines.Single(line => line.PurchaseOrderLineId == lines[0].Id).Quantity);
        Assert.Equal(2m, response.Receipt.Lines.Single(line => line.PurchaseOrderLineId == lines[1].Id).Quantity);
        Assert.Equal(2, response.Receipt.Movements.Count);
        Assert.All(response.Receipt.Lines, line => Assert.Equal(0m, line.OutstandingQuantityAfter));
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(3m, await StoredQuantityAsync(dbContext, other.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_skips_lines_that_are_already_fulfilled()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));
        await ReceiveAsync(dbContext, order.Id, (lines[0], 10m));

        var response = await ReceiveRemainingAsync(dbContext, order.Id);

        Assert.Equal(lines[1].Id, Assert.Single(response.Receipt.Lines).PurchaseOrderLineId);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(2m, await StoredQuantityAsync(dbContext, other.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_never_reposts_quantities_already_received()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await ReceiveRemainingAsync(dbContext, order.Id);

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => ReceiveRemainingAsync(dbContext, order.Id));

        Assert.Equal(PurchaseOrderReceiptBlockerCodes.OrderNotReceivable, Assert.Single(exception.Blockers).Code);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_defaults_the_received_time_to_now_and_records_optional_details()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var response = await ReceiveRemainingAsync(
            dbContext,
            order.Id,
            new ReceiveRemainingPurchaseOrderRequest { DeliveryReference = "DN-9", Notes = "Final pallet" });

        Assert.Equal(UtcNow, response.Receipt.ReceivedUtc);
        Assert.Equal("DN-9", response.Receipt.DeliveryReference);
        Assert.Equal("Final pallet", response.Receipt.Notes);
        Assert.Equal("Received from purchase order, delivery DN-9", Assert.Single(response.Receipt.Movements).Reason);
        Assert.NotEqual(Guid.Empty, response.Receipt.ClientRequestId);
    }

    [Fact]
    public async Task ReceiveRemainingAsync_honours_a_confirmed_remaining_preview()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 5m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, Clock)
            .GetRemainingPreviewAsync(order.Id, CancellationToken.None);
        var request = new ReceiveRemainingPurchaseOrderRequest
        {
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
        };
        item.ReduceStock(1m);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<PurchaseOrderReceiptStalePreviewException>(
            () => ReceiveRemainingAsync(dbContext, order.Id, request));

        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_replays_an_identical_client_request_after_the_order_closed()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var request = new ReceiveRemainingPurchaseOrderRequest { ClientRequestId = Guid.NewGuid() };
        var original = await ReceiveRemainingAsync(dbContext, order.Id, request);

        var repeat = await ReceiveRemainingAsync(dbContext, order.Id, request);

        Assert.True(repeat.AlreadyPosted);
        Assert.Equal(original.Receipt.ReceiptId, repeat.Receipt.ReceiptId);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_rejects_a_client_request_id_used_for_a_partial_receipt()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var partial = await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));

        await Assert.ThrowsAsync<PurchaseOrderReceiptRequestConflictException>(
            () => ReceiveRemainingAsync(
                dbContext,
                order.Id,
                new ReceiveRemainingPurchaseOrderRequest { ClientRequestId = partial.Receipt.ClientRequestId }));

        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_honours_an_expected_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var staleToken = order.ConcurrencyToken;
        await ReceiveAsync(dbContext, order.Id, (lines[0], 4m));

        await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(
            () => ReceiveRemainingAsync(
                dbContext,
                order.Id,
                new ReceiveRemainingPurchaseOrderRequest { ExpectedConcurrencyToken = staleToken }));

        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_rejects_invalid_optional_fields()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var exception = await Assert.ThrowsAsync<ReceiveRemainingPurchaseOrderValidationException>(
            () => ReceiveRemainingAsync(
                dbContext,
                order.Id,
                new ReceiveRemainingPurchaseOrderRequest { ReceivedUtc = UtcNow.AddDays(1) }));

        Assert.Equal(["receivedUtc"], exception.Errors.Keys);
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_leaves_a_legacy_received_order_and_its_stock_untouched()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 7m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await PurchaseOrderTestData.SeedLegacyReceivedAsync(dbContext, order, lines);

        await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => ReceiveRemainingAsync(dbContext, order.Id));

        Assert.Equal(7m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(dbContext, order.Id));
        Assert.Equal(0, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task ToStatusChangeResponse_maps_a_receipt_for_the_receive_endpoint()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 1m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, _) = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Ordered,
            (item, 3m),
            (item, 5m),
            (other, 2m));

        var receipt = await ReceiveRemainingAsync(dbContext, order.Id);

        var response = PostPurchaseOrderReceiptService.ToStatusChangeResponse(receipt);
        var itemAdjustment = response.StockAdjustments.Single(adjustment => adjustment.InventoryItemId == item.Id);

        Assert.Equal(order.Id, response.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), response.PreviousStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), response.Status);
        Assert.Empty(response.AllowedTransitions);
        Assert.Same(receipt.Receipt, response.Receipt);
        Assert.False(response.AlreadyPosted);
        Assert.Equal(2, response.StockAdjustments.Count);
        Assert.Equal(8m, itemAdjustment.QuantityReceived);
        Assert.Equal(9m, itemAdjustment.QuantityOnHand);
        Assert.Equal(2m, response.StockAdjustments.Single(adjustment => adjustment.InventoryItemId == other.Id).QuantityOnHand);
    }

    [Fact]
    public async Task PostAsync_reports_a_conflict_when_stock_changes_while_it_is_saved()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 5m);
        var (order, lines) = await SeedOrderAsync(seedContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(seedContext, order.Id, (lines[0], 4m));
        var interceptor = new CompetingChangeInterceptor(() => UseStockAsync(databaseName, item.Id, 1m));
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 4m))));

        Assert.Equal(order.Id, exception.PurchaseOrderId);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(4m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(0m, (await verification.Set<PurchaseOrderLine>().SingleAsync()).ReceivedQuantity);
        Assert.Equal(0, await ReceiptCountAsync(verification, order.Id));
        Assert.Equal(0, await MovementCountAsync(verification));
    }

    [Fact]
    public async Task PostAsync_replays_when_a_competing_request_with_the_same_client_id_wins()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(seedContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var request = Request(await PreviewAsync(seedContext, order.Id, (lines[0], 4m)), (lines[0], 4m));
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await PostAsync(competitor, order.Id, request);
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        var response = await PostAsync(dbContext, order.Id, request);

        Assert.True(response.AlreadyPosted);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(4m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(1, await ReceiptCountAsync(verification, order.Id));
        Assert.Equal(1, await MovementCountAsync(verification));
    }

    [Fact]
    public async Task PostAsync_reports_a_conflict_when_a_competing_receipt_changed_the_order_first()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(seedContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var preview = await PreviewAsync(seedContext, order.Id, (lines[0], 6m));
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await PostAsync(competitor, order.Id, Request(preview, (lines[0], 6m)));
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(
            () => PostAsync(dbContext, order.Id, Request(preview, (lines[0], 6m))));

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(6m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(6m, (await verification.Set<PurchaseOrderLine>().SingleAsync()).ReceivedQuantity);
        Assert.Equal(1, await ReceiptCountAsync(verification, order.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_reports_a_conflict_instead_of_receiving_what_a_competing_receipt_took()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(seedContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await ReceiveAsync(competitor, order.Id, (lines[0], 3m));
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(() => ReceiveRemainingAsync(dbContext, order.Id));

        // A second call re-reads the order and receives only what the competing receipt left outstanding.
        await using var retryContext = CreateDbContext(databaseName);
        var retried = await ReceiveRemainingAsync(retryContext, order.Id);

        Assert.Equal(7m, Assert.Single(retried.Receipt.Lines).Quantity);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(10m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(2, await ReceiptCountAsync(verification, order.Id));
    }

    [Fact]
    public async Task ReceiveRemainingAsync_reports_a_conflict_when_a_competing_cancellation_wins()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 2m);
        var (order, _) = await SeedOrderAsync(seedContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await new ChangePurchaseOrderStatusService(competitor).CancelAsync(order.Id, CancellationToken.None);
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(() => ReceiveRemainingAsync(dbContext, order.Id));

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(2m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(0, await ReceiptCountAsync(verification, order.Id));
        Assert.Equal(PurchaseOrderStatus.Cancelled, (await verification.Set<PurchaseOrder>().SingleAsync()).Status);
        await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => ReceiveRemainingAsync(verification, order.Id));
    }

    // Simulates part usage committing between our read and our save.
    private static async Task UseStockAsync(string databaseName, Guid inventoryItemId, decimal quantity)
    {
        await using var competitor = CreateDbContext(databaseName);
        var item = await competitor.Set<InventoryItem>().SingleAsync(candidate => candidate.Id == inventoryItemId);
        item.ReduceStock(quantity);
        await competitor.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateInterceptedDbContext(string databaseName, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptor)
            .Options;

        return new HarborOpsDbContext(options);
    }

    // Lets a competing request commit just before this context saves, then fails the save the way SQL Server
    // rejects a stale concurrency token. The in-memory provider checks neither tokens nor unique indexes, so
    // the rejection is raised here; the database side is covered by the relational tests.
    private sealed class CompetingChangeInterceptor(Func<Task> competingChange) : SaveChangesInterceptor
    {
        private bool competed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!competed)
            {
                competed = true;
                await competingChange();

                throw new DbUpdateConcurrencyException("A competing request changed the same rows first.");
            }

            return result;
        }
    }
}
