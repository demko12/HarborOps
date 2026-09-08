using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetWorkOrderPartReservationsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task GetReservations_returns_every_quantity_for_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Main engine filter", "FILTER-001", quantity: 10m);
        var reservation = await SeedReservationAsync(
            dbContext,
            workOrder.Id,
            filter.Id,
            requested: 6m,
            available: 10m);

        reservation.Consume(2m, UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(filter.Id, item.InventoryItemId);
        Assert.Equal("Main engine filter", item.Name);
        Assert.Equal("FILTER-001", item.PartNumber);
        Assert.Equal(6m, item.QuantityRequested);
        Assert.Equal(6m, item.QuantityReserved);
        Assert.Equal(2m, item.QuantityConsumed);
        Assert.Equal(4m, item.QuantityRemaining);
        Assert.Equal(0m, item.ShortageQuantity);
        Assert.False(item.HasShortage);
    }

    [Fact]
    public async Task GetReservations_reports_the_work_order_status_and_actions()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal("Open", response.Status);
        Assert.Equal(["Start", "Cancel", "ReplaceEquipment"], response.AllowedActions);
        Assert.Empty(response.Items);
        Assert.False(response.HasShortages);
        Assert.Equal(UtcNow, response.RetrievedUtc);
    }

    [Fact]
    public async Task Availability_subtracts_active_holds_from_other_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.InProgress);
        var filter = await SeedItemAsync(dbContext, "Main engine filter", "FILTER-001", quantity: 10m);

        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 3m, available: 10m);
        await SeedReservationAsync(dbContext, other.Id, filter.Id, requested: 4m, available: 7m);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(10m, item.StockOnHand);
        Assert.Equal(3m, item.AvailableQuantity);
    }

    [Fact]
    public async Task Availability_ignores_holds_from_finished_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var canceled = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Canceled);
        var filter = await SeedItemAsync(dbContext, "Main engine filter", "FILTER-001", quantity: 10m);

        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 3m, available: 10m);
        await SeedReservationAsync(dbContext, canceled.Id, filter.Id, requested: 5m, available: 7m);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(7m, item.AvailableQuantity);
    }

    [Fact]
    public async Task Availability_discounts_what_the_reservation_already_consumed()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Main engine filter", "FILTER-001", quantity: 10m);
        var reservation = await SeedReservationAsync(
            dbContext,
            workOrder.Id,
            filter.Id,
            requested: 6m,
            available: 10m);

        reservation.Consume(6m, UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(0m, item.QuantityRemaining);
        Assert.Equal(10m, item.AvailableQuantity);
    }

    [Fact]
    public async Task GetReservations_flags_shortages()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var covered = await SeedItemAsync(dbContext, "Bearing", "PN-1", quantity: 10m);
        var short_ = await SeedItemAsync(dbContext, "Gasket", "PN-2", quantity: 1m);

        await SeedReservationAsync(dbContext, workOrder.Id, covered.Id, requested: 2m, available: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, short_.Id, requested: 4m, available: 1m);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        Assert.True(response.HasShortages);
        Assert.False(response.Items[0].HasShortage);
        Assert.True(response.Items[1].HasShortage);
        Assert.Equal(3m, response.Items[1].ShortageQuantity);
    }

    [Fact]
    public async Task GetReservations_orders_by_name_then_part_number()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var gasket = await SeedItemAsync(dbContext, "Gasket", "PN-3", quantity: 10m);
        var bearing = await SeedItemAsync(dbContext, "Bearing", "PN-1", quantity: 10m);
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-2", quantity: 10m);

        await SeedReservationAsync(dbContext, workOrder.Id, gasket.Id, requested: 1m, available: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, bearing.Id, requested: 1m, available: 10m);
        await SeedReservationAsync(dbContext, workOrder.Id, filter.Id, requested: 1m, available: 10m);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        Assert.Equal(
            [bearing.Id, filter.Id, gasket.Id],
            response.Items.Select(item => item.InventoryItemId));
    }

    [Fact]
    public async Task GetReservations_excludes_other_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        var filter = await SeedItemAsync(dbContext, "Main engine filter", "FILTER-001", quantity: 10m);

        await SeedReservationAsync(dbContext, other.Id, filter.Id, requested: 3m, available: 10m);

        var response = await CreateQuery(dbContext).GetReservationsAsync(
            workOrder.Id,
            CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetReservations_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateQuery(dbContext).GetReservationsAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
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

    private static GetWorkOrderPartReservationsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetWorkOrderPartReservationsQuery(dbContext, new FixedTimeProvider(FixedNow));
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
