using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ReplaceWorkOrderPartReservationsServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task ReplaceReservations_reserves_the_requested_quantity()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var response = await CreateService(dbContext).ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(filter.Id, item.InventoryItemId);
        Assert.Equal(4m, item.QuantityRequested);
        Assert.Equal(4m, item.QuantityReserved);
        Assert.Equal(0m, item.QuantityConsumed);
        Assert.Equal(4m, item.QuantityRemaining);
        Assert.Equal(0m, item.ShortageQuantity);
        Assert.False(item.HasShortage);
        Assert.False(response.HasShortages);
        Assert.Equal("Open", response.Status);
        Assert.Equal(UtcNow, response.UpdatedUtc);
    }

    [Fact]
    public async Task ReplaceReservations_does_not_touch_stock_on_hand()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var response = await CreateService(dbContext).ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var stored = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal(10m, stored.Quantity);
        Assert.Equal(10m, response.Items[0].StockOnHand);
        Assert.Equal(6m, response.Items[0].AvailableQuantity);
    }

    [Fact]
    public async Task ReplaceReservations_reports_a_shortage_when_stock_falls_short()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 3m);

        var response = await CreateService(dbContext).ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 5m)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(5m, item.QuantityRequested);
        Assert.Equal(3m, item.QuantityReserved);
        Assert.Equal(2m, item.ShortageQuantity);
        Assert.True(item.HasShortage);
        Assert.True(response.HasShortages);
    }

    [Fact]
    public async Task ReplaceReservations_adds_updates_and_removes_in_one_call()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var kept = await SeedItemAsync(dbContext, "Bearing", "PN-1", quantity: 10m);
        var dropped = await SeedItemAsync(dbContext, "Filter", "PN-2", quantity: 10m);
        var added = await SeedItemAsync(dbContext, "Gasket", "PN-3", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((kept.Id, 2m), (dropped.Id, 3m)),
            CancellationToken.None);

        var response = await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((kept.Id, 5m), (added.Id, 1m)),
            CancellationToken.None);

        var stored = await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(kept.Id, response.Items[0].InventoryItemId);
        Assert.Equal(5m, response.Items[0].QuantityRequested);
        Assert.Equal(added.Id, response.Items[1].InventoryItemId);
        Assert.Equal(2, stored.Count);
        Assert.DoesNotContain(stored, reservation => reservation.InventoryItemId == dropped.Id);
    }

    [Fact]
    public async Task ReplaceReservations_clears_every_reservation_for_an_empty_request()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var response = await service.ReplaceReservationsAsync(
            workOrder.Id,
            new ReplaceWorkOrderPartReservationsRequest { Reservations = [] },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.False(response.HasShortages);
        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReplaceReservations_orders_rows_by_name_then_part_number()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var gasket = await SeedItemAsync(dbContext, "Gasket", "PN-3", quantity: 10m);
        var bearing = await SeedItemAsync(dbContext, "Bearing", "PN-1", quantity: 10m);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-2", quantity: 10m);

        var response = await CreateService(dbContext).ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((gasket.Id, 1m), (filter.Id, 1m), (bearing.Id, 1m)),
            CancellationToken.None);

        Assert.Equal(
            [bearing.Id, filter.Id, gasket.Id],
            response.Items.Select(item => item.InventoryItemId));
    }

    [Fact]
    public async Task ReplaceReservations_rejects_a_missing_reservation_list()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderPartReservationsValidationException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                new ReplaceWorkOrderPartReservationsRequest(),
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderPartReservationsValidator.ReservationsField, exception.Errors);
    }

    [Fact]
    public async Task ReplaceReservations_rejects_duplicate_inventory_items()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderPartReservationsValidationException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((filter.Id, 1m), (filter.Id, 2m)),
                CancellationToken.None));

        Assert.Contains(
            "duplicate",
            exception.Errors[ReplaceWorkOrderPartReservationsValidator.ReservationsField][0],
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ReplaceReservations_rejects_a_quantity_that_is_not_positive(int quantity)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderPartReservationsValidationException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((filter.Id, quantity)),
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderPartReservationsValidator.ReservationsField, exception.Errors);
    }

    [Fact]
    public async Task ReplaceReservations_rejects_an_empty_inventory_item_id()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderPartReservationsValidationException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((Guid.Empty, 1m)),
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderPartReservationsValidator.ReservationsField, exception.Errors);
    }

    [Fact]
    public async Task ReplaceReservations_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                missingId,
                RequestFor((Guid.NewGuid(), 1m)),
                CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task ReplaceReservations_rejects_unknown_inventory_items()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<WorkOrderPartReservationItemsNotFoundException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((filter.Id, 1m), (missingId, 1m)),
                CancellationToken.None));

        Assert.Equal([missingId], exception.InventoryItemIds);
        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task ReplaceReservations_rejects_a_work_order_that_is_not_open(WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, status);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorkOrderReservationChangeNotAllowedException>(
            () => CreateService(dbContext).ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((filter.Id, 1m)),
                CancellationToken.None));

        Assert.Equal(status, exception.CurrentStatus);
        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Reservations_across_work_orders_never_exceed_the_stock_on_hand()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var second = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            first.Id,
            RequestFor((filter.Id, 8m)),
            CancellationToken.None);

        var response = await service.ReplaceReservationsAsync(
            second.Id,
            RequestFor((filter.Id, 5m)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        var totalReserved = await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .SumAsync(reservation => reservation.QuantityReserved - reservation.QuantityConsumed);

        Assert.Equal(5m, item.QuantityRequested);
        Assert.Equal(2m, item.QuantityReserved);
        Assert.Equal(3m, item.ShortageQuantity);
        Assert.Equal(0m, item.AvailableQuantity);
        Assert.Equal(10m, totalReserved);
    }

    [Fact]
    public async Task Holds_from_finished_work_orders_do_not_block_new_reservations()
    {
        await using var dbContext = CreateDbContext();
        var canceled = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var open = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            canceled.Id,
            RequestFor((filter.Id, 8m)),
            CancellationToken.None);

        var tracked = await dbContext.Set<MaintenanceWorkOrder>()
            .SingleAsync(candidate => candidate.Id == canceled.Id);
        tracked.Cancel();
        await dbContext.SaveChangesAsync();

        var response = await service.ReplaceReservationsAsync(
            open.Id,
            RequestFor((filter.Id, 9m)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(9m, item.QuantityReserved);
        Assert.False(item.HasShortage);
    }

    [Fact]
    public async Task ReplaceReservations_refuses_to_remove_a_consumed_reservation()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);
        var gasket = await SeedItemAsync(dbContext, "Gasket", "PN-2", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var reservation = await dbContext.Set<WorkOrderPartReservation>().SingleAsync();
        reservation.Consume(2m, UtcNow);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<WorkOrderPartReservationConflictException>(
            () => service.ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((gasket.Id, 1m)),
                CancellationToken.None));

        Assert.Contains("already consumed", exception.Message, StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync();

        Assert.Single(stored);
        Assert.Equal(filter.Id, stored[0].InventoryItemId);
    }

    [Fact]
    public async Task ReplaceReservations_refuses_a_request_below_the_consumed_quantity()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var reservation = await dbContext.Set<WorkOrderPartReservation>().SingleAsync();
        reservation.Consume(3m, UtcNow);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<WorkOrderPartReservationConflictException>(
            () => service.ReplaceReservationsAsync(
                workOrder.Id,
                RequestFor((filter.Id, 1m)),
                CancellationToken.None));

        Assert.Contains("consumed quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceReservations_keeps_the_consumed_quantity_when_the_request_grows()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);

        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 4m)),
            CancellationToken.None);

        var reservation = await dbContext.Set<WorkOrderPartReservation>().SingleAsync();
        reservation.Consume(2m, UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((filter.Id, 6m)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(6m, item.QuantityRequested);
        Assert.Equal(6m, item.QuantityReserved);
        Assert.Equal(2m, item.QuantityConsumed);
        Assert.Equal(4m, item.QuantityRemaining);
    }

    [Fact]
    public async Task ReplaceReservations_replaces_the_reservation_token_of_added_and_removed_items()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var dropped = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 10m);
        var added = await SeedItemAsync(dbContext, "Gasket", "PN-2", quantity: 10m);
        var droppedStockToken = dropped.StockConcurrencyToken;
        var addedStockToken = added.StockConcurrencyToken;
        var service = CreateService(dbContext);

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((dropped.Id, 2m)),
            CancellationToken.None);

        var droppedReservationToken = dropped.ReservationConcurrencyToken;
        var addedReservationToken = added.ReservationConcurrencyToken;

        await service.ReplaceReservationsAsync(
            workOrder.Id,
            RequestFor((added.Id, 1m)),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var storedDropped = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == dropped.Id);
        var storedAdded = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == added.Id);

        Assert.NotEqual(droppedReservationToken, storedDropped.ReservationConcurrencyToken);
        Assert.NotEqual(addedReservationToken, storedAdded.ReservationConcurrencyToken);
        Assert.Equal(droppedStockToken, storedDropped.StockConcurrencyToken);
        Assert.Equal(addedStockToken, storedAdded.StockConcurrencyToken);
    }

    private static ReplaceWorkOrderPartReservationsRequest RequestFor(
        params (Guid InventoryItemId, decimal Quantity)[] entries)
    {
        return new ReplaceWorkOrderPartReservationsRequest
        {
            Reservations = entries
                .Select(entry => new WorkOrderPartReservationEntry
                {
                    InventoryItemId = entry.InventoryItemId,
                    Quantity = entry.Quantity,
                })
                .ToList(),
        };
    }

    private static async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        HarborOpsDbContext dbContext,
        WorkOrderStatus status)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            UtcNow.AddDays(7));

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(UtcNow);
                break;

            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;

            default:
                break;
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    private static async Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        string name,
        string partNumber,
        decimal quantity)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity: 2m,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static ReplaceWorkOrderPartReservationsService CreateService(HarborOpsDbContext dbContext)
    {
        return new ReplaceWorkOrderPartReservationsService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
