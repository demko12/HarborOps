using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetInventoryItemsQueryTests
{
    [Fact]
    public async Task GetInventoryItemsAsync_returns_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        for (var index = 1; index <= 26; index++)
        {
            await inventoryItems.AddAsync(CreateItem($"Part {index:D2}", $"PN-{index:D2}"));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Part 01", response.Items[0].Name);
        Assert.Equal("Part 25", response.Items[^1].Name);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_applies_custom_paging()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        for (var index = 1; index <= 5; index++)
        {
            await inventoryItems.AddAsync(CreateItem($"Part {index:D2}", $"PN-{index:D2}"));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Part 03", response.Items[0].Name);
        Assert.Equal("Part 04", response.Items[1].Name);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_orders_by_name_then_part_number_then_id()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        var tiedA = CreateItem("Gasket", "PN-B");
        var tiedB = CreateItem("Gasket", "PN-A");
        var earliest = CreateItem("Bearing", "PN-C");

        await inventoryItems.AddRangeAsync(tiedA, tiedB, earliest);
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest(),
            CancellationToken.None);

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(earliest.Id, response.Items[0].InventoryItemId);
        Assert.Equal(tiedB.Id, response.Items[1].InventoryItemId);
        Assert.Equal(tiedA.Id, response.Items[2].InventoryItemId);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_filters_by_search_on_name_or_part_number()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        var byName = CreateItem("Fuel Filter", "AB-1000");
        var byPartNumber = CreateItem("Oil Filter", "Fuel-9000");
        var other = CreateItem("Impeller", "IMP-1");

        await inventoryItems.AddRangeAsync(byName, byPartNumber, other);
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest { Search = "Fuel" },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Contains(response.Items, item => item.InventoryItemId == byName.Id);
        Assert.Contains(response.Items, item => item.InventoryItemId == byPartNumber.Id);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_filters_low_stock_true()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        var lowStockAtMinimum = CreateItem("Low A", "PN-LOW-A", quantity: 5m, minimumQuantity: 5m);
        var lowStockBelowMinimum = CreateItem("Low B", "PN-LOW-B", quantity: 2m, minimumQuantity: 5m);
        var healthyStock = CreateItem("Healthy", "PN-HEALTHY", quantity: 20m, minimumQuantity: 5m);

        await inventoryItems.AddRangeAsync(lowStockAtMinimum, lowStockBelowMinimum, healthyStock);
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest { LowStock = true },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Contains(response.Items, item => item.InventoryItemId == lowStockAtMinimum.Id);
        Assert.Contains(response.Items, item => item.InventoryItemId == lowStockBelowMinimum.Id);
        Assert.DoesNotContain(response.Items, item => item.InventoryItemId == healthyStock.Id);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_filters_low_stock_false()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        var lowStock = CreateItem("Low", "PN-LOW", quantity: 1m, minimumQuantity: 5m);
        var healthyStock = CreateItem("Healthy", "PN-HEALTHY", quantity: 20m, minimumQuantity: 5m);

        await inventoryItems.AddRangeAsync(lowStock, healthyStock);
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest { LowStock = false },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(healthyStock.Id, Assert.Single(response.Items).InventoryItemId);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_projects_scalar_category_unit_and_supplier_ids()
    {
        await using var dbContext = CreateDbContext();
        var inventoryItems = dbContext.Set<InventoryItem>();

        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        var item = InventoryItem.Create(
            categoryId,
            unitId,
            supplierId,
            "Bearing",
            "PN-BEARING",
            quantity: 10m,
            minimumQuantity: 2m,
            unitCost: 15.5m);

        await inventoryItems.AddAsync(item);
        await dbContext.SaveChangesAsync();

        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(categoryId, row.CategoryId);
        Assert.Equal(unitId, row.UnitId);
        Assert.Equal(supplierId, row.SupplierId);
        Assert.Equal(item.PartNumber, row.PartNumber);
        Assert.Equal(item.Quantity, row.Quantity);
        Assert.Equal(item.MinimumQuantity, row.MinimumQuantity);
        Assert.Equal(item.UnitCost, row.UnitCost);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_returns_empty_when_no_items_match()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetInventoryItemsQuery(dbContext);

        var response = await query.GetInventoryItemsAsync(
            new GetInventoryItemsRequest { Search = "Nonexistent" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(1, response.PageNumber);
        Assert.Equal(GetInventoryItemsValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetInventoryItemsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetInventoryItemsValidationException>(
            () => query.GetInventoryItemsAsync(
                new GetInventoryItemsRequest { PageSize = GetInventoryItemsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetInventoryItemsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetInventoryItemsAsync_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetInventoryItemsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetInventoryItemsValidationException>(
            () => query.GetInventoryItemsAsync(
                new GetInventoryItemsRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(GetInventoryItemsValidator.PageNumberField, exception.Errors);
    }

    private static InventoryItem CreateItem(
        string name,
        string partNumber,
        decimal quantity = 10m,
        decimal minimumQuantity = 2m)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity,
            unitCost: 9.99m);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
