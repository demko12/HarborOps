using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ReleaseWorkOrderPartReservationsServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task ReleaseAsync_removes_reservations_that_were_never_consumed()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 4m, available: 10m);

        await CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReleaseAsync_keeps_the_consumed_quantity_and_drops_only_the_hold()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        var reservation = await SeedReservationAsync(
            dbContext,
            workOrder.Id,
            filter.Id,
            requested: 4m,
            available: 10m);

        reservation.Consume(3m, UtcNow);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var stored = Assert.Single(
            await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());

        Assert.Equal(3m, stored.QuantityRequested);
        Assert.Equal(3m, stored.QuantityReserved);
        Assert.Equal(3m, stored.QuantityConsumed);
        Assert.Equal(0m, stored.QuantityRemaining);
    }

    [Fact]
    public async Task ReleaseAsync_frees_the_stock_for_other_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var released = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, released.Id, filter.Id, requested: 8m, available: 10m);
        await SeedReservationAsync(dbContext, other.Id, filter.Id, requested: 2m, available: 2m);

        await CreateService(dbContext).ReleaseAsync(released.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var remainingHolds = await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .SumAsync(reservation => reservation.QuantityReserved - reservation.QuantityConsumed);

        Assert.Equal(2m, remainingHolds);
    }

    [Fact]
    public async Task ReleaseAsync_never_touches_stock_on_hand()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 4m, available: 10m);

        await CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal(10m, stored.Quantity);
    }

    [Fact]
    public async Task ReleaseAsync_is_allowed_for_a_canceled_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Canceled);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 4m, available: 10m);
        dbContext.ChangeTracker.Clear();

        await CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    public async Task ReleaseAsync_rejects_a_work_order_that_is_neither_open_nor_canceled(
        WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, status);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 4m, available: 10m);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorkOrderReservationReleaseNotAllowedException>(
            () => CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None));

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(status, exception.CurrentStatus);

        dbContext.ChangeTracker.Clear();

        Assert.Single(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReleaseAsync_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateService(dbContext).ReleaseAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task ReleaseAsync_succeeds_when_there_is_nothing_to_release()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);

        await CreateService(dbContext).ReleaseAsync(workOrder.Id, CancellationToken.None);

        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReleaseAsync_leaves_other_work_orders_untouched()
    {
        await using var dbContext = CreateDbContext();
        var released = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, quantity: 10m);
        await SeedReservationAsync(dbContext, released.Id, filter.Id, requested: 4m, available: 10m);
        await SeedReservationAsync(dbContext, other.Id, filter.Id, requested: 3m, available: 6m);

        await CreateService(dbContext).ReleaseAsync(released.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var stored = Assert.Single(
            await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());

        Assert.Equal(other.Id, stored.WorkOrderId);
        Assert.Equal(3m, stored.QuantityReserved);
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
        decimal quantity)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            quantity,
            minimumQuantity: 2m,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static async Task<WorkOrderPartReservation> SeedReservationAsync(
        HarborOpsDbContext dbContext,
        Guid workOrderId,
        Guid inventoryItemId,
        decimal requested,
        decimal available)
    {
        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            inventoryItemId,
            requested,
            available,
            UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        return reservation;
    }

    private static ReleaseWorkOrderPartReservationsService CreateService(HarborOpsDbContext dbContext)
    {
        return new ReleaseWorkOrderPartReservationsService(dbContext, new FixedTimeProvider(FixedNow));
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
