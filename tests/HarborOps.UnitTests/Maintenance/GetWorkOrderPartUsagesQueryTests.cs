using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetWorkOrderPartUsagesQueryTests
{
    [Fact]
    public async Task GetPartUsagesAsync_returns_usage_rows_with_remaining_quantity()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem("Main engine filter", "FILTER-001", quantity: 7.5m);
        var usage = InventoryItemUsage.Create(workOrder.Id, inventoryItem.Id, 2.5m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<InventoryItemUsage>().AddAsync(usage);
        await dbContext.SaveChangesAsync();

        var query = new GetWorkOrderPartUsagesQuery(dbContext);

        var response = await query.GetPartUsagesAsync(workOrder.Id, CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(inventoryItem.Id, row.PartId);
        Assert.Equal("Main engine filter", row.Name);
        Assert.Equal("FILTER-001", row.PartNumber);
        Assert.Equal(2.5m, row.QuantityUsed);
        Assert.Equal(7.5m, row.RemainingQuantity);
    }

    [Fact]
    public async Task GetPartUsagesAsync_returns_empty_list_when_no_usage_recorded()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var query = new GetWorkOrderPartUsagesQuery(dbContext);

        var response = await query.GetPartUsagesAsync(workOrder.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetPartUsagesAsync_throws_when_work_order_is_unknown()
    {
        await using var dbContext = CreateDbContext();

        var query = new GetWorkOrderPartUsagesQuery(dbContext);

        await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => query.GetPartUsagesAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetPartUsagesAsync_only_returns_usage_for_the_requested_work_order()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var otherWorkOrder = CreateWorkOrder();
        var inventoryItem = CreateInventoryItem("Main engine filter", "FILTER-001", quantity: 10m);
        var otherItem = CreateInventoryItem("Oil filter", "FILTER-002", quantity: 5m);

        var usage = InventoryItemUsage.Create(workOrder.Id, inventoryItem.Id, 1m);
        var otherUsage = InventoryItemUsage.Create(otherWorkOrder.Id, otherItem.Id, 3m);

        await dbContext.Set<MaintenanceWorkOrder>().AddRangeAsync(workOrder, otherWorkOrder);
        await dbContext.Set<InventoryItem>().AddRangeAsync(inventoryItem, otherItem);
        await dbContext.Set<InventoryItemUsage>().AddRangeAsync(usage, otherUsage);
        await dbContext.SaveChangesAsync();

        var query = new GetWorkOrderPartUsagesQuery(dbContext);

        var response = await query.GetPartUsagesAsync(workOrder.Id, CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(inventoryItem.Id, row.PartId);
    }

    [Fact]
    public async Task GetPartUsagesAsync_orders_by_name_then_part_number_then_id()
    {
        await using var dbContext = CreateDbContext();

        var workOrder = CreateWorkOrder();
        var tiedA = CreateInventoryItem("Gasket", "PN-B", quantity: 10m);
        var tiedB = CreateInventoryItem("Gasket", "PN-A", quantity: 10m);
        var earliest = CreateInventoryItem("Bearing", "PN-C", quantity: 10m);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddRangeAsync(tiedA, tiedB, earliest);
        await dbContext.Set<InventoryItemUsage>().AddRangeAsync(
            InventoryItemUsage.Create(workOrder.Id, tiedA.Id, 1m),
            InventoryItemUsage.Create(workOrder.Id, tiedB.Id, 1m),
            InventoryItemUsage.Create(workOrder.Id, earliest.Id, 1m));
        await dbContext.SaveChangesAsync();

        var query = new GetWorkOrderPartUsagesQuery(dbContext);

        var response = await query.GetPartUsagesAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(earliest.Id, response.Items[0].PartId);
        Assert.Equal(tiedB.Id, response.Items[1].PartId);
        Assert.Equal(tiedA.Id, response.Items[2].PartId);
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

    private static InventoryItem CreateInventoryItem(string name, string partNumber, decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
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
