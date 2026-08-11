using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RecordInventoryItemUsageServiceTests
{
    [Fact]
    public async Task RecordUsage_reduces_inventory_and_persists_usage()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem(10m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync();

        var service = new RecordInventoryItemUsageService(dbContext);
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
        var service = new RecordInventoryItemUsageService(dbContext);
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
        var service = new RecordInventoryItemUsageService(dbContext);
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

        var service = new RecordInventoryItemUsageService(dbContext);
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

        var service = new RecordInventoryItemUsageService(dbContext);
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

        var service = new RecordInventoryItemUsageService(dbContext);
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
