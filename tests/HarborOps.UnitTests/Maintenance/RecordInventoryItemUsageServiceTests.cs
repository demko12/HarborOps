using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RecordInventoryItemUsageServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task RecordUsage_reduces_inventory_and_persists_usage()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var request = CreateValidRequest(2.500m);

        var response = await service.RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            request,
            CancellationToken.None);

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(CancellationToken.None);

        var usage = await dbContext
            .Set<InventoryItemUsage>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal(inventoryItem.Id, response.PartId);
        Assert.Equal(2.500m, response.QuantityUsed);
        Assert.Equal(7.500m, response.RemainingQuantity);

        Assert.Equal(7.500m, savedInventoryItem.Quantity);

        Assert.Equal(workOrder.Id, usage.WorkOrderId);
        Assert.Equal(inventoryItem.Id, usage.PartId);
        Assert.Equal(2.500m, usage.QuantityUsed);
    }

    [Fact]
    public async Task RecordUsage_rejects_zero_quantity()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = CreateValidRequest(0m);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageValidationException>(
            () => service.RecordUsageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request,
                CancellationToken.None));

        Assert.Contains(
            RecordInventoryItemUsageValidator.QuantityUsedField,
            exception.Errors);
    }

    [Fact]
    public async Task RecordUsage_rejects_negative_quantity()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = CreateValidRequest(-1m);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageValidationException>(
            () => service.RecordUsageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request,
                CancellationToken.None));

        Assert.Contains(
            RecordInventoryItemUsageValidator.QuantityUsedField,
            exception.Errors);
    }

    [Fact]
    public async Task RecordUsage_rejects_insufficient_stock()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(2m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var request = CreateValidRequest(3m);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageConflictException>(
            () => service.RecordUsageAsync(
                workOrder.Id,
                inventoryItem.Id,
                request,
                CancellationToken.None));

        Assert.Contains("Insufficient stock", exception.Message);

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2m, savedInventoryItem.Quantity);
        Assert.Empty(usages);
    }

    [Fact]
    public async Task RecordUsage_rejects_invalid_work_order_state()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        workOrder.Cancel();

        var inventoryItem = CreateInventoryItem(10m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var request = CreateValidRequest(2m);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageConflictException>(
            () => service.RecordUsageAsync(
                workOrder.Id,
                inventoryItem.Id,
                request,
                CancellationToken.None));

        Assert.Contains("Open or InProgress", exception.Message);

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .ToListAsync(CancellationToken.None);

        Assert.Equal(10m, savedInventoryItem.Quantity);
        Assert.Empty(usages);
    }

    [Fact]
    public async Task RecordUsage_rejects_duplicate_usage()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);

        var existingUsage = InventoryItemUsage.Create(
            workOrder.Id,
            inventoryItem.Id,
            2m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<InventoryItemUsage>().AddAsync(existingUsage);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var request = CreateValidRequest(3m);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageConflictException>(
            () => service.RecordUsageAsync(
                workOrder.Id,
                inventoryItem.Id,
                request,
                CancellationToken.None));

        Assert.Contains("already exists", exception.Message);

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .ToListAsync(CancellationToken.None);

        Assert.Equal(10m, savedInventoryItem.Quantity);
        Assert.Single(usages);
        Assert.Equal(2m, usages[0].QuantityUsed);
    }

    [Fact]
    public async Task RecordUsage_consumes_the_reservation_and_the_stock_together()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var reservation = WorkOrderPartReservation.Create(
            workOrder.Id,
            inventoryItem.Id,
            quantityRequested: 6m,
            availableQuantity: 10m,
            UtcNow);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var response = await service.RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            CreateValidRequest(4m),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();
        var storedReservation = await dbContext
            .Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(6m, response.RemainingQuantity);
        Assert.Equal(6m, storedItem.Quantity);
        Assert.Equal(6m, storedReservation.QuantityReserved);
        Assert.Equal(4m, storedReservation.QuantityConsumed);
        Assert.Equal(2m, storedReservation.QuantityRemaining);
        Assert.Equal(UtcNow, storedReservation.UpdatedUtc);
    }

    [Fact]
    public async Task RecordUsage_rejects_more_than_the_reserved_quantity()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var reservation = WorkOrderPartReservation.Create(
            workOrder.Id,
            inventoryItem.Id,
            quantityRequested: 3m,
            availableQuantity: 10m,
            UtcNow);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordInventoryItemUsageConflictException>(
            () => service.RecordUsageAsync(
                workOrder.Id,
                inventoryItem.Id,
                CreateValidRequest(5m),
                CancellationToken.None));

        Assert.Contains(
            "exceeds the remaining reserved quantity",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_usage_leaves_stock_and_reservation_untouched()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var reservation = WorkOrderPartReservation.Create(
            workOrder.Id,
            inventoryItem.Id,
            quantityRequested: 3m,
            availableQuantity: 10m,
            UtcNow);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RecordInventoryItemUsageConflictException>(
            () => service.RecordUsageAsync(
                workOrder.Id,
                inventoryItem.Id,
                CreateValidRequest(5m),
                CancellationToken.None));

        dbContext.ChangeTracker.Clear();

        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();
        var storedReservation = await dbContext
            .Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(10m, storedItem.Quantity);
        Assert.Equal(0m, storedReservation.QuantityConsumed);
        Assert.Empty(await dbContext.Set<InventoryItemUsage>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Usage_without_a_reservation_keeps_the_existing_behaviour()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var response = await service.RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            CreateValidRequest(4m),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        Assert.Equal(6m, response.RemainingQuantity);
        Assert.Empty(await dbContext.Set<WorkOrderPartReservation>().AsNoTracking().ToListAsync());
        Assert.Single(await dbContext.Set<InventoryItemUsage>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_reservation_for_another_work_order_is_not_consumed()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var otherWorkOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var reservation = WorkOrderPartReservation.Create(
            otherWorkOrder.Id,
            inventoryItem.Id,
            quantityRequested: 6m,
            availableQuantity: 10m,
            UtcNow);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(otherWorkOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await service.RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            CreateValidRequest(2m),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var storedReservation = await dbContext
            .Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(0m, storedReservation.QuantityConsumed);
    }

    [Fact]
    public async Task RecordUsage_replaces_the_stock_and_reservation_tokens()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var stockToken = inventoryItem.StockConcurrencyToken;
        var reservationToken = inventoryItem.ReservationConcurrencyToken;
        var reservation = WorkOrderPartReservation.Create(
            workOrder.Id,
            inventoryItem.Id,
            quantityRequested: 6m,
            availableQuantity: 10m,
            UtcNow);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            CreateValidRequest(4m),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.NotEqual(stockToken, storedItem.StockConcurrencyToken);
        Assert.NotEqual(reservationToken, storedItem.ReservationConcurrencyToken);
    }

    [Fact]
    public async Task Usage_without_a_reservation_replaces_only_the_stock_token()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);
        var stockToken = inventoryItem.StockConcurrencyToken;
        var reservationToken = inventoryItem.ReservationConcurrencyToken;

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).RecordUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            CreateValidRequest(4m),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.NotEqual(stockToken, storedItem.StockConcurrencyToken);
        Assert.Equal(reservationToken, storedItem.ReservationConcurrencyToken);
    }

    private static RecordInventoryItemUsageRequest CreateValidRequest(
        decimal quantityUsed)
    {
        return new RecordInventoryItemUsageRequest
        {
            QuantityUsed = quantityUsed,
        };
    }

    private static MaintenanceWorkOrder CreateWorkOrder()
    {
        return MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));
    }

    private static InventoryItem CreateInventoryItem(decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            quantity,
            2m,
            150m);
    }

    private static RecordInventoryItemUsageService CreateService(HarborOpsDbContext dbContext)
    {
        return new RecordInventoryItemUsageService(dbContext, new FixedTimeProvider(FixedNow));
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
