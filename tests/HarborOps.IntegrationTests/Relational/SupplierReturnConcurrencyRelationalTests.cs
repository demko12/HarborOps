using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Races supplier-return posting against every other inventory movement. Every round must end in a
/// state that some serial order of the operations that succeeded could have produced.
/// </summary>
public sealed class SupplierReturnConcurrencyRelationalTests : IAsyncLifetime
{
    private const int Rounds = 5;
    private const int SqlServerDeadlockErrorNumber = 1205;

    private static readonly Guid ActorUserId =
        Guid.Parse("5e6f7081-92a3-4b4c-8d5e-6f708192a3b4");

    private static readonly DateTime UtcNow =
        new(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);

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
    public async Task Simultaneous_posts_of_one_return_move_stock_exactly_once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var order = await SeedReceivedOrderAsync((itemId, 5m));
            var supplierReturnId = await CreateReturnAsync(order.Id, (order.LineIds[0], 4m));
            var firstPreview = await PreviewAsync(supplierReturnId);
            var secondPreview = await PreviewAsync(supplierReturnId);

            var (first, second) = await RaceAsync(
                () => PostAsync(firstPreview),
                () => PostAsync(secondPreview));

            // A retry that loses the race reports the winner's result, so both may succeed.
            Assert.True(first is null || second is null, $"Round {round}: neither post succeeded.");
            AssertExpectedConflicts(first, second);
            Assert.Equal(6m, await StoredQuantityAsync(itemId));
            Assert.Equal(1, await MovementCountAsync(supplierReturnId));
            Assert.Equal(SupplierReturnStatus.Posted, await StoredStatusAsync(supplierReturnId));
        }
    }

    [RelationalFact]
    public async Task Competing_returns_never_exceed_the_received_quantity()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 20m);
            var order = await SeedReceivedOrderAsync((itemId, 5m));
            var firstReturnId = await CreateReturnAsync(order.Id, (order.LineIds[0], 3m));
            var secondReturnId = await CreateReturnAsync(order.Id, (order.LineIds[0], 3m));
            var firstPreview = await PreviewAsync(firstReturnId);
            var secondPreview = await PreviewAsync(secondReturnId);

            var (first, second) = await RaceAsync(
                () => PostAsync(firstPreview),
                () => PostAsync(secondPreview));

            AssertExpectedConflicts(first, second);

            var postedCount = new[] { first, second }.Count(outcome => outcome is null);

            Assert.True(postedCount <= 1, $"Round {round}: both returns posted 6 of 5 received.");
            Assert.Equal(20m - (3m * postedCount), await StoredQuantityAsync(itemId));
            Assert.True(await PostedQuantityAsync(order.LineIds[0]) <= 5m);
        }
    }

    [RelationalFact]
    public async Task Posting_and_part_usage_never_lose_an_update_or_split_a_return()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var freeItemId = await SeedItemAsync(quantity: 10m);
            var usedItemId = await SeedItemAsync(quantity: 10m);
            var workOrderId = await SeedWorkOrderAsync();
            var order = await SeedReceivedOrderAsync((freeItemId, 5m), (usedItemId, 5m));
            var supplierReturnId = await CreateReturnAsync(
                order.Id,
                (order.LineIds[0], 2m),
                (order.LineIds[1], 2m));
            var preview = await PreviewAsync(supplierReturnId);

            var (post, usage) = await RaceAsync(
                () => PostAsync(preview),
                () => RecordUsageAsync(workOrderId, usedItemId, 1m));

            AssertExpectedConflicts(post, usage);

            // A failed post must leave the untouched item unchanged too: the return is all or nothing.
            Assert.Equal(post is null ? 8m : 10m, await StoredQuantityAsync(freeItemId));
            Assert.Equal(
                10m - (post is null ? 2m : 0m) - (usage is null ? 1m : 0m),
                await StoredQuantityAsync(usedItemId));
            Assert.Equal(post is null ? 2 : 0, await MovementCountAsync(supplierReturnId));
        }
    }

    [RelationalFact]
    public async Task Posting_and_a_purchase_order_receipt_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var received = await SeedReceivedOrderAsync((itemId, 5m));
            var orderedId = await SeedOrderedPurchaseOrderAsync(itemId, quantity: 5m);
            var supplierReturnId = await CreateReturnAsync(received.Id, (received.LineIds[0], 2m));
            var preview = await PreviewAsync(supplierReturnId);

            var (post, receipt) = await RaceAsync(
                () => PostAsync(preview),
                () => ReceiveAsync(orderedId));

            AssertExpectedConflicts(post, receipt);

            var expected = 10m - (post is null ? 2m : 0m) + (receipt is null ? 5m : 0m);

            Assert.Equal(expected, await StoredQuantityAsync(itemId));
            Assert.Equal(post is null ? 1 : 0, await MovementCountAsync(supplierReturnId));
        }
    }

    [RelationalFact]
    public async Task Posting_and_a_reservation_change_never_return_reserved_stock()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var workOrderId = await SeedWorkOrderAsync();
            var order = await SeedReceivedOrderAsync((itemId, 10m));
            var supplierReturnId = await CreateReturnAsync(order.Id, (order.LineIds[0], 4m));
            var preview = await PreviewAsync(supplierReturnId);

            var (post, reservation) = await RaceAsync(
                () => PostAsync(preview),
                () => ReserveAsync(workOrderId, itemId, 9m));

            AssertExpectedConflicts(post, reservation);

            var quantity = await StoredQuantityAsync(itemId);
            var reserved = await OutstandingReservedAsync(itemId);

            Assert.Equal(post is null ? 6m : 10m, quantity);
            Assert.True(
                reserved <= quantity,
                $"Round {round}: {reserved} reserved exceeds {quantity} on hand.");
        }
    }

    [RelationalFact]
    public async Task Posting_and_a_stocktake_never_both_apply_to_the_same_stock()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 10m);
            var order = await SeedReceivedOrderAsync((itemId, 5m));
            var supplierReturnId = await CreateReturnAsync(order.Id, (order.LineIds[0], 2m));
            var stocktakeId = await SeedCountedStocktakeAsync(itemId, counted: 7m);
            var returnPreview = await PreviewAsync(supplierReturnId);
            var stocktakePreview = await PreviewStocktakeAsync(stocktakeId);

            var (post, stocktake) = await RaceAsync(
                () => PostAsync(returnPreview),
                () => PostStocktakeAsync(stocktakePreview));

            AssertExpectedConflicts(post, stocktake);

            Assert.False(post is null && stocktake is null, $"Round {round}: both applied to one baseline.");
            Assert.Equal(
                post is null ? 8m : stocktake is null ? 7m : 10m,
                await StoredQuantityAsync(itemId));
        }
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
            is SupplierReturnConcurrencyException
            or SupplierReturnStalePreviewException
            or SupplierReturnPostingBlockedException
            or StocktakeAlreadyPostedException
            or StocktakeConcurrencyException
            or StocktakeStalePreviewException
            or StocktakePostingBlockedException
            or RecordInventoryItemUsageConflictException
            or PurchaseOrderConflictException
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

    private async Task PostAsync(SupplierReturnPostingPreview preview)
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

    private async Task<SupplierReturnPostingPreview> PreviewAsync(Guid supplierReturnId)
    {
        await using var dbContext = CreateDbContext();

        return await new GetSupplierReturnPostingPreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(supplierReturnId, CancellationToken.None);
    }

    private async Task<Guid> CreateReturnAsync(Guid purchaseOrderId, params (Guid LineId, decimal Quantity)[] lines)
    {
        await using var dbContext = CreateDbContext();

        var details = await new CreateSupplierReturnService(dbContext, TimeProvider.System).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = purchaseOrderId,
                Lines = lines
                    .Select(line => new SupplierReturnLineEntry
                    {
                        PurchaseOrderLineId = line.LineId,
                        Quantity = line.Quantity,
                        Reason = "Damaged on arrival",
                    })
                    .ToList(),
            },
            ActorUserId,
            CancellationToken.None);

        return details.SupplierReturnId;
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

        await new ChangePurchaseOrderStatusService(dbContext)
            .ReceiveAsync(purchaseOrderId, CancellationToken.None);
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

    private async Task<(Guid Id, IReadOnlyList<Guid> LineIds)> SeedReceivedOrderAsync(
        params (Guid InventoryItemId, decimal Quantity)[] lines)
    {
        await using var dbContext = CreateDbContext();

        var supplierId = await dbContext.Set<InventoryItem>()
            .Where(item => item.Id == lines[0].InventoryItemId)
            .Select(item => item.SupplierId)
            .SingleAsync();
        var orderDate = DateOnly.FromDateTime(UtcNow);
        var order = PurchaseOrder.Create(supplierId, orderDate, orderDate.AddDays(7));
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        order.ChangeStatusTo(PurchaseOrderStatus.Received);
        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.InventoryItemId, line.Quantity, 25m))
            .ToList();

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return (order.Id, orderLines.Select(line => line.Id).ToList());
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

    private async Task<SupplierReturnStatus> StoredStatusAsync(Guid supplierReturnId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<SupplierReturn>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == supplierReturnId)
            .Select(candidate => candidate.Status)
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

    private async Task<decimal> PostedQuantityAsync(Guid purchaseOrderLineId)
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

    private async Task<int> MovementCountAsync(Guid supplierReturnId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.SupplierReturnId == supplierReturnId);
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
