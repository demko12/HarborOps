using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Races receipt posting against other receipts and every other inventory movement on SQL Server. Every
/// round must end in a state that some serial order of the operations that succeeded could have produced.
/// </summary>
public sealed class PurchaseOrderReceiptConcurrencyRelationalTests : IAsyncLifetime
{
    private const int Rounds = 5;
    private const int SqlServerDeadlockErrorNumber = 1205;
    private const int SqlServerTemporalPeriodErrorNumber = 13535;

    private static readonly Guid ActorUserId =
        Guid.Parse("7a8b9cad-becf-4d0e-9f1a-2b3c4d5e6f70");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    private static DateTime ReceivedUtc => DateTime.UtcNow.AddHours(-1);

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task Simultaneous_identical_requests_post_one_receipt_and_a_later_retry_reports_it()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var order = await SeedOrderedOrderAsync(itemId, 10m);
            var request = await RequestAsync(order, (0, 4m));

            var outcomes = await RaceAsync(Enumerable.Range(0, 4)
                .Select(_ => (Func<Task<PostPurchaseOrderReceiptResponse>>)(() => PostAsync(order.Id, request)))
                .ToArray());

            // A duplicate that loses the race either reports the winner's receipt or a conflict.
            AssertExpectedConflicts(outcomes.Select(outcome => outcome.Error));

            var succeeded = outcomes.Where(outcome => outcome.Error is null).Select(outcome => outcome.Result!).ToList();

            Assert.NotEmpty(succeeded);
            Assert.Single(succeeded, result => !result.AlreadyPosted);
            Assert.Single(succeeded.Select(result => result.Receipt.ReceiptId).Distinct());
            Assert.Equal(14m, await StoredQuantityAsync(itemId));
            Assert.Equal(4m, await ReceivedQuantityAsync(order.LineIds[0]));
            Assert.Equal(1, await ReceiptCountAsync(order.Id));
            await AssertReceiptTrailAddsUpAsync(order.Id);

            var retry = await PostAsync(order.Id, request);

            Assert.True(retry.AlreadyPosted, $"Round {round}: a retry after the race was not replayed.");
            Assert.Equal(succeeded[0].Receipt.ReceiptId, retry.Receipt.ReceiptId);
        }
    }

    [RelationalFact]
    public async Task Receipts_confirmed_from_the_same_preview_admit_exactly_one()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedOrderedOrderAsync(itemId, 10m);

            var confirmed = await RequestAsync(order, (0, 6m));

            var outcomes = await RaceAsync(Enumerable.Range(0, 3)
                .Select(_ => (Func<Task<PostPurchaseOrderReceiptResponse>>)(() =>
                    PostAsync(order.Id, confirmed with { ClientRequestId = Guid.NewGuid() })))
                .ToArray());

            Assert.Single(outcomes, outcome => outcome.Error is null);
            AssertExpectedConflicts(outcomes.Select(outcome => outcome.Error));
            Assert.Equal(6m, await StoredQuantityAsync(itemId));
            Assert.Equal(6m, await ReceivedQuantityAsync(order.LineIds[0]));
            Assert.Equal(1, await ReceiptCountAsync(order.Id));
            await AssertReceiptTrailAddsUpAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task Competing_receive_remaining_calls_never_over_receive()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 1m);
            var otherItemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedOrderedOrderAsync(itemId, 10m, otherItemId, 2m);

            var outcomes = await RaceAsync(Enumerable.Range(0, 3)
                .Select(_ => (Func<Task<PostPurchaseOrderReceiptResponse>>)(() => ReceiveRemainingAsync(order.Id)))
                .ToArray());

            Assert.Single(outcomes, outcome => outcome.Error is null);
            AssertExpectedConflicts(outcomes.Select(outcome => outcome.Error));
            Assert.Equal(11m, await StoredQuantityAsync(itemId));
            Assert.Equal(2m, await StoredQuantityAsync(otherItemId));
            Assert.Equal(PurchaseOrderStatus.Received, await StoredStatusAsync(order.Id));
            Assert.Equal(1, await ReceiptCountAsync(order.Id));
            await AssertReceiptTrailAddsUpAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task A_receipt_and_part_usage_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var workOrderId = await SeedWorkOrderAsync();
            var order = await SeedOrderedOrderAsync(itemId, 10m);
            var request = await RequestAsync(order, (0, 4m));

            var (receipt, usage) = await RaceAsync(
                () => PostAsync(order.Id, request),
                () => RecordUsageAsync(workOrderId, itemId, 1m));

            AssertExpectedConflicts([receipt, usage]);

            var received = receipt is null ? 4m : 0m;

            Assert.Equal(10m + received - (usage is null ? 1m : 0m), await StoredQuantityAsync(itemId));
            Assert.Equal(received, await ReceivedQuantityAsync(order.LineIds[0]));
            Assert.Equal(receipt is null ? 1 : 0, await ReceiptCountAsync(order.Id));
            await AssertReceiptTrailAddsUpAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task A_receipt_and_a_stocktake_never_both_apply_to_the_same_baseline()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var order = await SeedOrderedOrderAsync(itemId, 10m);
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 7m);
            var stocktakePreview = await PreviewStocktakeAsync(stocktakeId);
            var request = await RequestAsync(order, (0, 4m));

            var (receipt, stocktake) = await RaceAsync(
                () => PostAsync(order.Id, request),
                () => PostStocktakeAsync(stocktakePreview));

            AssertExpectedConflicts([receipt, stocktake]);

            // The stocktake counted the pre-receipt baseline, so it can only win if it committed first.
            var expected = (receipt, stocktake) switch
            {
                (null, null) => 11m,
                (null, _) => 14m,
                (_, null) => 7m,
                _ => 10m,
            };

            Assert.Equal(expected, await StoredQuantityAsync(itemId));
            Assert.Equal(receipt is null ? 4m : 0m, await ReceivedQuantityAsync(order.LineIds[0]));
        }
    }

    [RelationalFact]
    public async Task A_receipt_and_a_supplier_return_on_the_same_order_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedOrderedOrderAsync(itemId, 10m);
            await PostAsync(order.Id, await RequestAsync(order, (0, 4m)));
            var supplierReturnId = await CreateReturnAsync(order.Id, order.LineIds[0], 3m);
            var returnPreview = await PreviewReturnAsync(supplierReturnId);
            var request = await RequestAsync(order, (0, 6m));

            var (receipt, supplierReturn) = await RaceAsync(
                () => PostAsync(order.Id, request),
                () => PostReturnAsync(returnPreview));

            AssertExpectedConflicts([receipt, supplierReturn]);

            var expected = 4m + (receipt is null ? 6m : 0m) - (supplierReturn is null ? 3m : 0m);

            Assert.Equal(expected, await StoredQuantityAsync(itemId));
            Assert.Equal(receipt is null ? 10m : 4m, await ReceivedQuantityAsync(order.LineIds[0]));
            Assert.True(await PostedReturnQuantityAsync(order.LineIds[0]) <= await ReceivedQuantityAsync(order.LineIds[0]));
        }
    }

    [RelationalFact]
    public async Task A_receipt_and_a_cancellation_never_both_win()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 5m);
            var order = await SeedOrderedOrderAsync(itemId, 10m);

            var (receipt, cancellation) = await RaceAsync(
                () => ReceiveRemainingAsync(order.Id),
                () => CancelAsync(order.Id));

            AssertExpectedConflicts([receipt, cancellation]);
            Assert.True(receipt is null ^ cancellation is null, $"Round {round}: receipt and cancellation disagree.");

            if (receipt is null)
            {
                Assert.Equal(PurchaseOrderStatus.Received, await StoredStatusAsync(order.Id));
                Assert.Equal(15m, await StoredQuantityAsync(itemId));
            }
            else
            {
                Assert.Equal(PurchaseOrderStatus.Cancelled, await StoredStatusAsync(order.Id));
                Assert.Equal(5m, await StoredQuantityAsync(itemId));
                Assert.Equal(0, await ReceiptCountAsync(order.Id));
            }
        }
    }

    private sealed record OrderSeed(Guid Id, IReadOnlyList<Guid> LineIds);

    // Previews the receipt and returns the request a user would send to confirm exactly that preview.
    private async Task<PostPurchaseOrderReceiptRequest> RequestAsync(
        OrderSeed order,
        params (int LineIndex, decimal Quantity)[] lines)
    {
        var entries = lines
            .Select(line => new PurchaseOrderReceiptLineEntry
            {
                PurchaseOrderLineId = order.LineIds[line.LineIndex],
                Quantity = line.Quantity,
            })
            .ToList();

        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, TimeProvider.System).GetPreviewAsync(
            order.Id,
            new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = ReceivedUtc, Lines = entries },
            CancellationToken.None);

        return new PostPurchaseOrderReceiptRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            ReceivedUtc = ReceivedUtc,
            Lines = entries,
        };
    }

    private static async Task<IReadOnlyList<(T? Result, Exception? Error)>> RaceAsync<T>(params Func<Task<T>>[] operations)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(T? Result, Exception? Error)> RunAsync(Func<Task<T>> operation)
        {
            await start.Task;

            try
            {
                return (await operation(), null);
            }
            catch (Exception exception)
            {
                return (default, exception);
            }
        }

        var tasks = operations.Select(operation => Task.Run(() => RunAsync(operation))).ToList();
        start.SetResult();

        return await Task.WhenAll(tasks);
    }

    private static async Task<(Exception? First, Exception? Second)> RaceAsync(Func<Task> first, Func<Task> second)
    {
        var outcomes = await RaceAsync<bool>(
            async () =>
            {
                await first();
                return true;
            },
            async () =>
            {
                await second();
                return true;
            });

        return (outcomes[0].Error, outcomes[1].Error);
    }

    private static void AssertExpectedConflicts(IEnumerable<Exception?> outcomes)
    {
        foreach (var exception in outcomes.OfType<Exception>())
        {
            var expected = exception
                is PurchaseOrderReceiptConcurrencyException
                or PurchaseOrderReceiptPostingBlockedException
                or PurchaseOrderReceiptStalePreviewException
                or PurchaseOrderConflictException
                or StocktakeAlreadyPostedException
                or StocktakeConcurrencyException
                or StocktakeStalePreviewException
                or StocktakePostingBlockedException
                or SupplierReturnConcurrencyException
                or SupplierReturnStalePreviewException
                or SupplierReturnPostingBlockedException
                or RecordInventoryItemUsageConflictException
                or DbUpdateConcurrencyException
                || IsSqlConflict(exception);

            Assert.True(expected, $"Unexpected failure: {exception}");
        }
    }

    // The other inventory writers do not retry, so they may surface a deadlock or temporal clash directly.
    private static bool IsSqlConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: SqlServerDeadlockErrorNumber or SqlServerTemporalPeriodErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    private async Task AssertReceiptTrailAddsUpAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        var movements = await (
            from movement in dbContext.Set<InventoryAdjustment>().AsNoTracking()
            join line in dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking()
                on movement.PurchaseOrderReceiptLineId equals line.Id
            join receipt in dbContext.Set<PurchaseOrderReceipt>().AsNoTracking()
                on line.PurchaseOrderReceiptId equals receipt.Id
            where receipt.PurchaseOrderId == purchaseOrderId
            select new { movement.QuantityDelta, line.Quantity })
            .ToListAsync();
        var lineCount = await (
            from line in dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking()
            join receipt in dbContext.Set<PurchaseOrderReceipt>().AsNoTracking()
                on line.PurchaseOrderReceiptId equals receipt.Id
            where receipt.PurchaseOrderId == purchaseOrderId
            select line)
            .CountAsync();

        // Every receipt line moved stock exactly once, by exactly its quantity.
        Assert.Equal(lineCount, movements.Count);
        Assert.All(movements, movement => Assert.Equal(movement.Quantity, movement.QuantityDelta));
    }

    private async Task<PostPurchaseOrderReceiptResponse> PostAsync(Guid purchaseOrderId, PostPurchaseOrderReceiptRequest request)
    {
        await using var dbContext = CreateDbContext();

        return await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .PostAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    private async Task<PostPurchaseOrderReceiptResponse> ReceiveRemainingAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .ReceiveRemainingAsync(purchaseOrderId, null, ActorUserId, CancellationToken.None);
    }

    private async Task CancelAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        await new ChangePurchaseOrderStatusService(dbContext).CancelAsync(purchaseOrderId, CancellationToken.None);
    }

    private async Task RecordUsageAsync(Guid workOrderId, Guid inventoryItemId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        await new RecordInventoryItemUsageService(dbContext, TimeProvider.System).RecordUsageAsync(
            workOrderId,
            inventoryItemId,
            new RecordInventoryItemUsageRequest { QuantityUsed = quantity },
            CancellationToken.None);
    }

    private async Task<Guid> CreateReturnAsync(Guid purchaseOrderId, Guid purchaseOrderLineId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        var details = await new CreateSupplierReturnService(dbContext, TimeProvider.System).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = purchaseOrderId,
                Lines =
                [
                    new SupplierReturnLineEntry
                    {
                        PurchaseOrderLineId = purchaseOrderLineId,
                        Quantity = quantity,
                        Reason = "Damaged on arrival",
                    },
                ],
            },
            ActorUserId,
            CancellationToken.None);

        return details.SupplierReturnId;
    }

    private async Task<SupplierReturnPostingPreview> PreviewReturnAsync(Guid supplierReturnId)
    {
        await using var dbContext = CreateDbContext();

        return await new GetSupplierReturnPostingPreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(supplierReturnId, CancellationToken.None);
    }

    private async Task PostReturnAsync(SupplierReturnPostingPreview preview)
    {
        await using var dbContext = CreateDbContext();

        await new PostSupplierReturnService(dbContext, TimeProvider.System).PostAsync(
            preview.SupplierReturnId,
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);
    }

    private async Task<Guid> SeedCountedStocktakeAsync(Guid inventoryItemId, decimal counted)
    {
        await using var dbContext = CreateDbContext();

        var draft = await new CreateStocktakeService(dbContext, TimeProvider.System).CreateAsync(
            new CreateStocktakeRequest { InventoryItemIds = [inventoryItemId] },
            ActorUserId,
            CancellationToken.None);

        await new UpdateStocktakeCountsService(dbContext, TimeProvider.System).UpdateCountsAsync(
            draft.StocktakeId,
            new UpdateStocktakeCountsRequest
            {
                ExpectedConcurrencyToken = draft.ConcurrencyToken,
                Lines =
                [
                    new StocktakeCountEntry
                    {
                        InventoryItemId = inventoryItemId,
                        CountedQuantity = counted,
                        Reason = "Physical count",
                    },
                ],
            },
            ActorUserId,
            CancellationToken.None);

        return draft.StocktakeId;
    }

    private async Task<StocktakePostingPreview> PreviewStocktakeAsync(Guid stocktakeId)
    {
        await using var dbContext = CreateDbContext();

        return await new GetStocktakePostingPreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(stocktakeId, CancellationToken.None);
    }

    private async Task PostStocktakeAsync(StocktakePostingPreview preview)
    {
        await using var dbContext = CreateDbContext();

        await new PostStocktakeService(dbContext, TimeProvider.System).PostAsync(
            preview.StocktakeId,
            new PostStocktakeRequest
            {
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);
    }

    private async Task<Guid> SeedItemAsync(decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        var category = ItemCategory.Create($"Spares {Guid.NewGuid():N}", null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var supplier = Supplier.Register($"Supplier {Guid.NewGuid():N}", null, null, null, null);
        var item = InventoryItem.Create(
            category.Id,
            unit.Id,
            supplier.Id,
            "Cooling pump seal",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity: 1m,
            unitCost: 25m);

        await dbContext.AddRangeAsync(category, unit, supplier, item);
        await dbContext.SaveChangesAsync();

        return item.Id;
    }

    private async Task<Guid> SeedWorkOrderAsync()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace cooling pump seal",
            null,
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }

    private Task<OrderSeed> SeedOrderedOrderAsync(Guid inventoryItemId, decimal quantity)
    {
        return SeedOrderedOrderAsync([(inventoryItemId, quantity)]);
    }

    private Task<OrderSeed> SeedOrderedOrderAsync(
        Guid inventoryItemId,
        decimal quantity,
        Guid otherInventoryItemId,
        decimal otherQuantity)
    {
        return SeedOrderedOrderAsync([(inventoryItemId, quantity), (otherInventoryItemId, otherQuantity)]);
    }

    private async Task<OrderSeed> SeedOrderedOrderAsync(IReadOnlyList<(Guid InventoryItemId, decimal Quantity)> lines)
    {
        await using var dbContext = CreateDbContext();

        var supplierId = await dbContext.Set<InventoryItem>()
            .Where(item => item.Id == lines[0].InventoryItemId)
            .Select(item => item.SupplierId)
            .SingleAsync();
        var orderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var order = PurchaseOrder.Create(supplierId, orderDate, orderDate.AddDays(14));
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.InventoryItemId, line.Quantity, 25m))
            .ToList();

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return new OrderSeed(order.Id, orderLines.Select(line => line.Id).ToList());
    }

    private async Task<decimal> StoredQuantityAsync(Guid inventoryItemId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
    }

    private async Task<decimal> ReceivedQuantityAsync(Guid purchaseOrderLineId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.Id == purchaseOrderLineId)
            .Select(line => line.ReceivedQuantity)
            .SingleAsync();
    }

    private async Task<PurchaseOrderStatus> StoredStatusAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == purchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync();
    }

    private async Task<int> ReceiptCountAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .CountAsync(receipt => receipt.PurchaseOrderId == purchaseOrderId);
    }

    private async Task<decimal> PostedReturnQuantityAsync(Guid purchaseOrderLineId)
    {
        await using var dbContext = CreateDbContext();

        return await (
            from line in dbContext.Set<SupplierReturnLine>().AsNoTracking()
            join supplierReturn in dbContext.Set<SupplierReturn>().AsNoTracking()
                on line.SupplierReturnId equals supplierReturn.Id
            where line.PurchaseOrderLineId == purchaseOrderLineId
                && supplierReturn.Status == SupplierReturnStatus.Posted
            select line.Quantity)
            .SumAsync();
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
