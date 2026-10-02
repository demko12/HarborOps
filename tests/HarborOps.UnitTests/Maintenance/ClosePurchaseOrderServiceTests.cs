using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderClosureTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ClosePurchaseOrderServiceTests
{
    [Fact]
    public async Task CloseAsync_closes_the_remaining_six_of_ten_and_leaves_stock_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var tokenBefore = (await StoredOrderAsync(dbContext, order.Id)).ConcurrencyToken;
        var request = Request(await PreviewAsync(dbContext, order.Id)) with { Reason = $"  {Reason}  " };

        var response = await CloseAsync(dbContext, order.Id, request);

        var line = Assert.Single(response.Closure.Lines);

        Assert.False(response.AlreadyClosed);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), response.OrderStatus);
        Assert.NotEqual(tokenBefore, response.ConcurrencyToken);
        Assert.Equal([PurchaseOrderActions.CreateReturn], response.AllowedActions);
        Assert.Equal(order.Id, response.Closure.PurchaseOrderId);
        Assert.Equal(request.ClientRequestId, response.Closure.ClientRequestId);
        Assert.Equal(Reason, response.Closure.Reason);
        Assert.Equal(ActorUserId, response.Closure.ClosedByUserId);
        Assert.Equal(UtcNow, response.Closure.ClosedUtc);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.Closure.OrderStatusBefore);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), response.Closure.OrderStatusAfter);
        Assert.Equal(4m, response.Closure.TotalQuantityReceived);
        Assert.Equal(6m, response.Closure.TotalQuantityClosed);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(item.Id, line.InventoryItemId);
        Assert.Equal("Main engine filter", line.Name);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(0m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task CloseAsync_commits_the_evidence_line_quantities_and_status_together()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 2m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));

        var response = await CloseRemainingAsync(dbContext, order.Id);

        await using var verification = CreateDbContext(databaseName);
        var closure = await verification.Set<PurchaseOrderClosure>().SingleAsync();
        var closureLine = await verification.Set<PurchaseOrderClosureLine>().SingleAsync();
        var storedLine = (await StoredLinesAsync(verification, order.Id))[lines[0].Id];
        var storedOrder = await StoredOrderAsync(verification, order.Id);

        Assert.Equal(response.Closure.ClosureId, closure.Id);
        Assert.Equal(PurchaseOrderClosure.RequestHashLength, closure.RequestHash.Length);
        Assert.Equal(closure.Id, closureLine.PurchaseOrderClosureId);
        Assert.Equal(6m, closureLine.ClosedQuantity);
        Assert.Equal(10m, storedLine.QuantityOrdered);
        Assert.Equal(10m, storedLine.UnitCost);
        Assert.Equal(4m, storedLine.ReceivedQuantity);
        Assert.Equal(6m, storedLine.ClosedQuantity);
        Assert.Equal(0m, storedLine.OutstandingQuantity);
        Assert.Equal(PurchaseOrderStatus.ClosedShort, storedOrder.Status);
        Assert.Equal(response.ConcurrencyToken, storedOrder.ConcurrencyToken);
        Assert.Equal(6m, await StoredQuantityAsync(verification, item.Id));
    }

    [Fact]
    public async Task CloseAsync_leaves_posted_receipts_and_their_movements_untouched()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var receiptsBefore = await new GetPurchaseOrderReceiptsQuery(dbContext).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest(),
            CancellationToken.None);

        await CloseRemainingAsync(dbContext, order.Id);

        await using var verification = CreateDbContext(databaseName);
        var receiptsAfter = await new GetPurchaseOrderReceiptsQuery(verification).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest(),
            CancellationToken.None);

        var before = Assert.Single(receiptsBefore.Items);
        var after = Assert.Single(receiptsAfter.Items);

        Assert.Equal(before.ReceiptId, after.ReceiptId);
        Assert.Equal(before.OrderStatusAfter, after.OrderStatusAfter);
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(before.Movements, after.Movements);
        Assert.Equal(1, await PurchaseOrderReceiptTestData.MovementCountAsync(verification));
    }

    [Fact]
    public async Task CloseAsync_closes_every_line_including_fully_received_lines_and_lines_sharing_an_item()
    {
        await using var dbContext = CreateDbContext();
        var filter = await SeedItemAsync(dbContext, quantity: 1m, name: "Fuel filter");
        var impeller = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedReceivedAsync(
            dbContext,
            (filter, 10m, 4m),
            (filter, 5m, 5m),
            (filter, 2m, 0.5m),
            (impeller, 3m, 0m));

        var response = await CloseRemainingAsync(dbContext, order.Id);

        var closed = response.Closure.Lines.ToDictionary(line => line.PurchaseOrderLineId, line => line.ClosedQuantity);
        var storedLines = await StoredLinesAsync(dbContext, order.Id);

        Assert.Equal(4, response.Closure.Lines.Count);
        Assert.Equal(6m, closed[lines[0].Id]);
        Assert.Equal(0m, closed[lines[1].Id]);
        Assert.Equal(1.5m, closed[lines[2].Id]);
        Assert.Equal(3m, closed[lines[3].Id]);
        Assert.Equal(10.5m, response.Closure.TotalQuantityClosed);
        Assert.All(storedLines.Values, line => Assert.Equal(0m, line.OutstandingQuantity));
        Assert.Equal(10.5m, await StoredQuantityAsync(dbContext, filter.Id));
        Assert.Equal(0m, await StoredQuantityAsync(dbContext, impeller.Id));
        Assert.Equal(4, await ClosureLineCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_returns_the_original_result_for_an_identical_repeat()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id));
        var original = await CloseAsync(dbContext, order.Id, request);

        // The repeat still carries the tokens of the preview it was confirmed from.
        var repeat = await CloseAsync(dbContext, order.Id, request);

        Assert.True(repeat.AlreadyClosed);
        Assert.Equal(original.Closure, repeat.Closure with { Lines = original.Closure.Lines });
        Assert.Equal(original.Closure.Lines, repeat.Closure.Lines);
        Assert.Equal(original.ConcurrencyToken, repeat.ConcurrencyToken);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), repeat.OrderStatus);
        Assert.Equal(1, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_treats_a_padded_reason_as_the_same_request()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id));
        await CloseAsync(dbContext, order.Id, request);

        var repeat = await CloseAsync(dbContext, order.Id, request with { Reason = $"   {Reason} " });

        Assert.True(repeat.AlreadyClosed);
        Assert.Equal(1, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_rejects_a_reused_client_request_id_with_a_different_reason()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id));
        await CloseAsync(dbContext, order.Id, request);

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureRequestConflictException>(
            () => CloseAsync(dbContext, order.Id, request with { Reason = "Supplier went out of business" }));

        Assert.Equal(request.ClientRequestId, exception.ClientRequestId);
        Assert.Equal(1, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_rejects_a_client_request_id_already_used_on_another_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var (otherOrder, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id));
        await CloseAsync(dbContext, order.Id, request);
        var otherPreview = await PreviewAsync(dbContext, otherOrder.Id);

        await Assert.ThrowsAsync<PurchaseOrderClosureRequestConflictException>(
            () => CloseAsync(
                dbContext,
                otherOrder.Id,
                Request(otherPreview) with { ClientRequestId = request.ClientRequestId }));

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await StoredOrderAsync(dbContext, otherOrder.Id)).Status);
        Assert.Equal(1, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_rejects_a_stale_expected_token_and_returns_the_refreshed_preview()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var preview = await PreviewAsync(dbContext, order.Id);
        await PurchaseOrderReceiptTestData.ReceiveAsync(dbContext, order.Id, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureConcurrencyException>(
            () => CloseAsync(dbContext, order.Id, Request(preview)));

        var stored = await StoredOrderAsync(dbContext, order.Id);

        Assert.Equal(order.Id, exception.PurchaseOrderId);
        Assert.Equal(stored.ConcurrencyToken, exception.RefreshedPreview.ConcurrencyToken);
        Assert.Equal(5m, exception.RefreshedPreview.TotalQuantityReceived);
        Assert.Equal(5m, exception.RefreshedPreview.TotalQuantityToClose);
        Assert.True(exception.RefreshedPreview.CanClose);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, stored.Status);
        Assert.Equal(0, await ClosureCountAsync(dbContext));

        // Confirming the refreshed preview closes exactly what is still outstanding.
        var retried = await CloseAsync(dbContext, order.Id, Request(exception.RefreshedPreview));

        Assert.Equal(5m, retried.Closure.TotalQuantityClosed);
    }

    [Fact]
    public async Task CloseAsync_rejects_a_preview_made_stale_by_a_posted_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var preview = await PreviewAsync(dbContext, order.Id);
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureStalePreviewException>(
            () => CloseAsync(dbContext, order.Id, Request(preview)));

        Assert.Equal(order.Id, exception.PurchaseOrderId);
        Assert.NotEqual(preview.PreviewToken, exception.RefreshedPreview.PreviewToken);
        Assert.Equal(1m, Assert.Single(exception.RefreshedPreview.Lines).ReturnedQuantity);
        Assert.Equal(0, await ClosureCountAsync(dbContext));

        var retried = await CloseAsync(dbContext, order.Id, Request(exception.RefreshedPreview));

        var line = Assert.Single(retried.Closure.Lines);

        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
    }

    [Fact]
    public async Task CloseAsync_rejects_a_preview_token_that_does_not_match()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id)) with { PreviewToken = new string('0', 64) };

        await Assert.ThrowsAsync<PurchaseOrderClosureStalePreviewException>(() => CloseAsync(dbContext, order.Id, request));

        Assert.Equal(0, await ClosureCountAsync(dbContext));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.ClosedShort)]
    public async Task CloseAsync_rejects_orders_that_cannot_be_closed(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, status, (item, 10m));
        var tokenBefore = (await StoredOrderAsync(dbContext, order.Id)).ConcurrencyToken;

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureBlockedException>(
            () => CloseRemainingAsync(dbContext, order.Id));

        var stored = await StoredOrderAsync(dbContext, order.Id);

        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(exception.Blockers).Code);
        Assert.Equal(status, stored.Status);
        Assert.Equal(tokenBefore, stored.ConcurrencyToken);
        Assert.Equal(0, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task CloseAsync_rejects_a_partially_received_order_without_a_recorded_receipt()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.PartiallyReceived, (item, 10m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureBlockedException>(
            () => CloseRemainingAsync(dbContext, order.Id));

        Assert.Equal(PurchaseOrderClosureBlockerCodes.NoReceipts, Assert.Single(exception.Blockers).Code);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await StoredOrderAsync(dbContext, order.Id)).Status);
    }

    [Fact]
    public async Task CloseAsync_rejects_a_second_close_with_a_new_client_request_id()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        await CloseRemainingAsync(dbContext, order.Id);

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureBlockedException>(
            () => CloseRemainingAsync(dbContext, order.Id));

        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(exception.Blockers).Code);
        Assert.Equal(1, await ClosureCountAsync(dbContext));
        Assert.Equal(6m, (await GetClosureAsync(dbContext, order.Id)).TotalQuantityClosed);
    }

    [Fact]
    public async Task CloseAsync_rejects_an_invalid_request_before_touching_the_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(dbContext, order.Id)) with { Reason = "Late", ClientRequestId = null };

        var exception = await Assert.ThrowsAsync<ClosePurchaseOrderValidationException>(
            () => CloseAsync(dbContext, order.Id, request));

        Assert.Equal(
            [ClosePurchaseOrderValidator.ClientRequestIdField, ClosePurchaseOrderValidator.ReasonField],
            exception.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await StoredOrderAsync(dbContext, order.Id)).Status);
    }

    [Fact]
    public async Task CloseAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();
        var purchaseOrderId = Guid.NewGuid();
        var request = new ClosePurchaseOrderRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = "ABC",
            Reason = Reason,
        };

        var exception = await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => CloseAsync(dbContext, purchaseOrderId, request));

        Assert.Equal(purchaseOrderId, exception.PurchaseOrderId);
    }

    [Fact]
    public async Task Receipts_of_every_kind_are_rejected_once_the_order_is_closed()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var receiptPreviewBeforeClose = await PurchaseOrderReceiptTestData.PreviewAsync(dbContext, order.Id, (lines[0], 1m));
        await CloseRemainingAsync(dbContext, order.Id);

        var partialPreview = await PurchaseOrderReceiptTestData.PreviewAsync(dbContext, order.Id, (lines[0], 1m));
        var remainingPreview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, Clock)
            .GetRemainingPreviewAsync(order.Id, CancellationToken.None);

        Assert.False(partialPreview.CanPost);
        Assert.Contains(
            partialPreview.Blockers,
            blocker => blocker.Code == PurchaseOrderReceiptBlockerCodes.OrderNotReceivable);
        Assert.False(remainingPreview.CanPost);
        Assert.Contains(
            remainingPreview.Blockers,
            blocker => blocker.Code == PurchaseOrderReceiptBlockerCodes.OrderNotReceivable);

        await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => PurchaseOrderReceiptTestData.PostAsync(
                dbContext,
                order.Id,
                PurchaseOrderReceiptTestData.Request(partialPreview, (lines[0], 1m))));
        await Assert.ThrowsAsync<PurchaseOrderReceiptConcurrencyException>(
            () => PurchaseOrderReceiptTestData.PostAsync(
                dbContext,
                order.Id,
                PurchaseOrderReceiptTestData.Request(receiptPreviewBeforeClose, (lines[0], 1m))));
        await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => PurchaseOrderReceiptTestData.ReceiveRemainingAsync(dbContext, order.Id));
        await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext).CancelAsync(order.Id, CancellationToken.None));

        Assert.Equal(PurchaseOrderStatus.ClosedShort, (await StoredOrderAsync(dbContext, order.Id)).Status);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await PurchaseOrderReceiptTestData.ReceiptCountAsync(dbContext, order.Id));
    }

    [Fact]
    public async Task A_return_from_a_closed_order_never_reopens_the_closed_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        await CloseRemainingAsync(dbContext, order.Id);

        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var details = await new GetPurchaseOrderDetailsQuery(dbContext, Clock)
            .GetDetailsAsync(order.Id, CancellationToken.None);
        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), details.Status);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
        Assert.Equal(0m, line.OutstandingQuantity);
        Assert.Equal(3m, line.ReturnableQuantity);
        Assert.Equal(0m, details.TotalQuantityOutstanding);
        Assert.Equal(3m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await ClosureCountAsync(dbContext));
    }

    [Fact]
    public async Task A_return_from_a_closed_order_is_capped_by_received_less_posted_returns()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        await CloseRemainingAsync(dbContext, order.Id);
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 3.001m)));
        var accepted = await CreateReturnAsync(dbContext, order, (lines[0], 3m));

        Assert.Equal(["lines[0].quantity"], exception.Errors.Keys);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), accepted.PurchaseOrderStatus);
        Assert.Empty(accepted.Blockers);
    }

    [Fact]
    public async Task A_return_from_a_closed_order_keeps_reservation_protection_and_the_reservations()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var workOrder = await SupplierReturnTestData.SeedReservationAsync(dbContext, item, 4m);
        await CloseRemainingAsync(dbContext, order.Id);
        var supplierReturn = await SupplierReturnTestData.SeedReturnAsync(dbContext, order, (lines[0], 1m));

        var preview = await SupplierReturnTestData.PreviewAsync(dbContext, supplierReturn.Id);

        var blocker = Assert.Single(preview.Blockers);
        var reservation = await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().SingleAsync();

        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
        Assert.Equal(4m, reservation.QuantityReserved);
        Assert.Equal(0m, reservation.QuantityConsumed);
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task CloseAsync_reports_a_conflict_and_closes_nothing_when_a_competing_receipt_wins()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(seedContext, (item, 10m, 4m));
        var preview = await PreviewAsync(seedContext, order.Id);
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await PurchaseOrderReceiptTestData.ReceiveAsync(competitor, order.Id, (lines[0], 2m));
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureConcurrencyException>(
            () => CloseAsync(dbContext, order.Id, Request(preview)));

        Assert.Equal(6m, exception.RefreshedPreview.TotalQuantityReceived);
        Assert.Equal(4m, exception.RefreshedPreview.TotalQuantityToClose);

        await using var verification = CreateDbContext(databaseName);
        var storedLine = Assert.Single((await StoredLinesAsync(verification, order.Id)).Values);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await StoredOrderAsync(verification, order.Id)).Status);
        Assert.Equal(6m, storedLine.ReceivedQuantity);
        Assert.Equal(0m, storedLine.ClosedQuantity);
        Assert.Equal(6m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(0, await ClosureCountAsync(verification));
        Assert.Equal(0, await ClosureLineCountAsync(verification));
    }

    [Fact]
    public async Task CloseAsync_replays_when_a_competing_request_with_the_same_client_id_wins()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(seedContext, (item, 10m, 4m));
        var request = Request(await PreviewAsync(seedContext, order.Id));
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await CloseAsync(competitor, order.Id, request);
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        var response = await CloseAsync(dbContext, order.Id, request);

        Assert.True(response.AlreadyClosed);
        Assert.Equal(6m, response.Closure.TotalQuantityClosed);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(1, await ClosureCountAsync(verification));
        Assert.Equal(1, await ClosureLineCountAsync(verification));
    }

    [Fact]
    public async Task CloseAsync_reports_a_conflict_when_a_competing_close_with_another_client_id_wins()
    {
        var databaseName = NewDatabaseName();
        await using var seedContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(seedContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(seedContext, (item, 10m, 4m));
        var preview = await PreviewAsync(seedContext, order.Id);
        var interceptor = new CompetingChangeInterceptor(async () =>
        {
            await using var competitor = CreateDbContext(databaseName);
            await CloseAsync(competitor, order.Id, Request(preview));
        });
        await using var dbContext = CreateInterceptedDbContext(databaseName, interceptor);

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureConcurrencyException>(
            () => CloseAsync(dbContext, order.Id, Request(preview)));

        Assert.False(exception.RefreshedPreview.CanClose);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), exception.RefreshedPreview.OrderStatus);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(1, await ClosureCountAsync(verification));
        Assert.Equal(6m, Assert.Single((await StoredLinesAsync(verification, order.Id)).Values).ClosedQuantity);
    }

    private static Task<SupplierReturnDetails> CreateReturnAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        return new CreateSupplierReturnService(dbContext, Clock).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = order.Id,
                Lines = [.. lines.Select(line => SupplierReturnTestData.Entry(line.Line, line.Quantity))],
            },
            ActorUserId,
            CancellationToken.None);
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
    // rejects a stale concurrency token or a duplicate closure. The in-memory provider checks neither tokens
    // nor unique indexes, so the rejection is raised here.
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
