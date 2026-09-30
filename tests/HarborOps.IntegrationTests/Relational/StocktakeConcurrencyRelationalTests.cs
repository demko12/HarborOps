using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Races real transactions against each other. Every round must end in a state that some serial
/// order of the operations that succeeded could have produced.
/// </summary>
public sealed class StocktakeConcurrencyRelationalTests : IAsyncLifetime
{
    private const int Rounds = 5;
    private const int SqlServerDeadlockErrorNumber = 1205;

    private static readonly Guid ActorUserId =
        Guid.Parse("3c4d5e6f-7081-492a-8b3c-4d5e6f708192");

    private static readonly DateTime UtcNow =
        new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

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
    public async Task Simultaneous_posts_adjust_stock_exactly_once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 6m);
            var firstPreview = await PreviewAsync(stocktakeId);
            var secondPreview = await PreviewAsync(stocktakeId);

            var (first, second) = await RaceAsync(
                () => PostAsync(firstPreview),
                () => PostAsync(secondPreview));

            Assert.Equal(1, new[] { first, second }.Count(outcome => outcome is null));
            AssertExpectedConflict(first ?? second!);
            Assert.Equal(6m, await StoredQuantityAsync(itemId));
            Assert.Equal(1, await AdjustmentCountAsync(stocktakeId));
        }
    }

    [RelationalFact]
    public async Task Posting_and_part_usage_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var workOrderId = await SeedWorkOrderAsync();
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 8m);
            var preview = await PreviewAsync(stocktakeId);

            var (post, usage) = await RaceAsync(
                () => PostAsync(preview),
                () => RecordUsageAsync(workOrderId, itemId, 1m));

            AssertExpectedConflicts(post, usage);

            // Serial orders: post then usage (8 - 1), usage then a rejected post (10 - 1), or post alone (8).
            var expected = (post is null ? 8m : 10m) - (usage is null ? 1m : 0m);

            Assert.Equal(expected, await StoredQuantityAsync(itemId));
            Assert.Equal(post is null ? 1 : 0, await AdjustmentCountAsync(stocktakeId));
        }
    }

    [RelationalFact]
    public async Task Posting_and_a_purchase_order_receipt_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var purchaseOrderId = await SeedOrderedPurchaseOrderAsync(itemId, quantity: 5m);
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 8m);
            var preview = await PreviewAsync(stocktakeId);

            var (post, receipt) = await RaceAsync(
                () => PostAsync(preview),
                () => ReceiveAsync(purchaseOrderId));

            AssertExpectedConflicts(post, receipt);

            var expected = (post is null ? 8m : 10m) + (receipt is null ? 5m : 0m);

            Assert.Equal(expected, await StoredQuantityAsync(itemId));
            Assert.Equal(post is null ? 1 : 0, await AdjustmentCountAsync(stocktakeId));
        }
    }

    [RelationalFact]
    public async Task Posting_and_a_reservation_change_never_leave_reservations_above_stock()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var workOrderId = await SeedWorkOrderAsync();
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 8m);
            var preview = await PreviewAsync(stocktakeId);

            var (post, reservation) = await RaceAsync(
                () => PostAsync(preview),
                () => ReserveAsync(workOrderId, itemId, 9m));

            AssertExpectedConflicts(post, reservation);

            var quantity = await StoredQuantityAsync(itemId);
            var reserved = await OutstandingReservedAsync(itemId);

            Assert.Equal(post is null ? 8m : 10m, quantity);
            Assert.True(
                reserved <= quantity,
                $"Round {round}: {reserved} reserved exceeds {quantity} on hand.");
        }
    }

    [RelationalFact]
    public async Task Concurrent_usages_never_oversell_the_last_part()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 1m);
            var firstWorkOrderId = await SeedWorkOrderAsync();
            var secondWorkOrderId = await SeedWorkOrderAsync();

            var (first, second) = await RaceAsync(
                () => RecordUsageAsync(firstWorkOrderId, itemId, 1m),
                () => RecordUsageAsync(secondWorkOrderId, itemId, 1m));

            Assert.Equal(1, new[] { first, second }.Count(outcome => outcome is null));
            AssertExpectedConflict(first ?? second!);
            Assert.Equal(0m, await StoredQuantityAsync(itemId));
        }
    }

    [RelationalFact]
    public async Task Concurrent_receipts_of_one_purchase_order_add_stock_once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var purchaseOrderId = await SeedOrderedPurchaseOrderAsync(itemId, quantity: 5m);

            var (first, second) = await RaceAsync(
                () => ReceiveAsync(purchaseOrderId),
                () => ReceiveAsync(purchaseOrderId));

            Assert.Equal(1, new[] { first, second }.Count(outcome => outcome is null));
            AssertExpectedConflict(first ?? second!);
            Assert.Equal(15m, await StoredQuantityAsync(itemId));
        }
    }

    [RelationalFact]
    public async Task A_stale_count_is_rejected_even_when_the_quantity_was_restored()
    {
        var itemId = await SeedItemAsync(quantity: 10m);

        await using var staleContext = CreateDbContext();
        var staleItem = await staleContext.Set<InventoryItem>()
            .SingleAsync(item => item.Id == itemId);

        await using (var winnerContext = CreateDbContext())
        {
            var winnerItem = await winnerContext.Set<InventoryItem>()
                .SingleAsync(item => item.Id == itemId);

            winnerItem.ReduceStock(2m);
            winnerItem.IncreaseStock(2m);
            await winnerContext.SaveChangesAsync();
        }

        staleItem.ApplyStocktakeCount(8m);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
        Assert.Equal(10m, await StoredQuantityAsync(itemId));
    }

    private static async Task<(Exception? First, Exception? Second)> RaceAsync(
        Func<Task> first,
        Func<Task> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> RunAsync(Func<Task> operation)
        {
            await start.Task;

            try
            {
                await operation();

                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var firstTask = Task.Run(() => RunAsync(first));
        var secondTask = Task.Run(() => RunAsync(second));
        start.SetResult();

        return (await firstTask, await secondTask);
    }

    private static void AssertExpectedConflicts(params Exception?[] outcomes)
    {
        foreach (var outcome in outcomes.Where(outcome => outcome is not null))
        {
            AssertExpectedConflict(outcome!);
        }
    }

    private static void AssertExpectedConflict(Exception exception)
    {
        var expected = exception
            is StocktakeAlreadyPostedException
            or StocktakeConcurrencyException
            or StocktakeStalePreviewException
            or StocktakePostingBlockedException
            or RecordInventoryItemUsageConflictException
            or PurchaseOrderConflictException
            or PurchaseOrderReceiptPostingBlockedException
            or PurchaseOrderReceiptConcurrencyException
            or WorkOrderPartReservationConflictException
            or DbUpdateConcurrencyException
            || IsDeadlockVictim(exception);

        Assert.True(expected, $"Unexpected failure: {exception}");
    }

    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: SqlServerDeadlockErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    private async Task PostAsync(StocktakePostingPreview preview)
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

    private async Task RecordUsageAsync(Guid workOrderId, Guid inventoryItemId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        await new RecordInventoryItemUsageService(dbContext, TimeProvider.System).RecordUsageAsync(
            workOrderId,
            inventoryItemId,
            new RecordInventoryItemUsageRequest { QuantityUsed = quantity },
            CancellationToken.None);
    }

    private async Task ReceiveAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .ReceiveRemainingAsync(purchaseOrderId, null, ActorUserId, CancellationToken.None);
    }

    private async Task ReserveAsync(Guid workOrderId, Guid inventoryItemId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        await new ReplaceWorkOrderPartReservationsService(dbContext, TimeProvider.System)
            .ReplaceReservationsAsync(
                workOrderId,
                new ReplaceWorkOrderPartReservationsRequest
                {
                    Reservations =
                    [
                        new WorkOrderPartReservationEntry
                        {
                            InventoryItemId = inventoryItemId,
                            Quantity = quantity,
                        },
                    ],
                },
                CancellationToken.None);
    }

    private async Task<StocktakePostingPreview> PreviewAsync(Guid stocktakeId)
    {
        await using var dbContext = CreateDbContext();

        return await new GetStocktakePostingPreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(stocktakeId, CancellationToken.None);
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
            UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }

    private async Task<Guid> SeedOrderedPurchaseOrderAsync(Guid inventoryItemId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        var supplierId = await dbContext.Set<InventoryItem>()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.SupplierId)
            .SingleAsync();
        var orderDate = DateOnly.FromDateTime(UtcNow);
        var order = PurchaseOrder.Create(supplierId, orderDate, orderDate.AddDays(7));
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddAsync(
            PurchaseOrderLine.Create(order.Id, inventoryItemId, quantity, 25m));
        await dbContext.SaveChangesAsync();

        return order.Id;
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

    private async Task<decimal> OutstandingReservedAsync(Guid inventoryItemId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.InventoryItemId == inventoryItemId)
            .SumAsync(reservation => reservation.QuantityReserved - reservation.QuantityConsumed);
    }

    private async Task<int> AdjustmentCountAsync(Guid stocktakeId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.StocktakeId == stocktakeId);
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
