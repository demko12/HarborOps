using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreatePurchaseOrderServiceTests
{
    private static readonly DateOnly OrderDate = new(2026, 8, 20);

    [Fact]
    public async Task CreateAsync_persists_a_draft_order_with_its_lines_and_total()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var filter = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");
        var impeller = await SeedInventoryItemAsync(dbContext, supplier.Id, "Impeller", "PN-2");

        var response = await new CreatePurchaseOrderService(dbContext).CreateAsync(
            new CreatePurchaseOrderRequest
            {
                SupplierId = supplier.Id,
                OrderDate = OrderDate,
                ExpectedDelivery = OrderDate.AddDays(10),
                Lines =
                [
                    new CreatePurchaseOrderLineRequest
                    {
                        InventoryItemId = filter.Id,
                        QuantityOrdered = 4m,
                        UnitCost = 25.50m,
                    },
                    new CreatePurchaseOrderLineRequest
                    {
                        InventoryItemId = impeller.Id,
                        QuantityOrdered = 2.500m,
                        UnitCost = 100m,
                    },
                ],
            },
            CancellationToken.None);

        var storedOrder = await dbContext.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedLines = await dbContext.Set<PurchaseOrderLine>().ToListAsync(CancellationToken.None);

        Assert.Equal(storedOrder.Id, response.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.Draft), response.Status);
        Assert.Equal(2, response.LineCount);
        Assert.Equal(352m, response.TotalCost);

        Assert.Equal(supplier.Id, storedOrder.SupplierId);
        Assert.Equal(OrderDate, storedOrder.OrderDate);
        Assert.Equal(OrderDate.AddDays(10), storedOrder.ExpectedDelivery);
        Assert.Equal(PurchaseOrderStatus.Draft, storedOrder.Status);

        Assert.Equal(2, storedLines.Count);
        Assert.All(storedLines, line => Assert.Equal(storedOrder.Id, line.PurchaseOrderId));
        Assert.Contains(storedLines, line => line.PartId == filter.Id && line.QuantityOrdered == 4m);
        Assert.Contains(storedLines, line => line.PartId == impeller.Id && line.UnitCost == 100m);
    }

    [Fact]
    public async Task CreateAsync_accepts_an_order_without_an_expected_delivery()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        await new CreatePurchaseOrderService(dbContext).CreateAsync(
            CreateRequest(supplier.Id, (item.Id, 1m, 10m)),
            CancellationToken.None);

        var storedOrder = await dbContext.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);

        Assert.Null(storedOrder.ExpectedDelivery);
    }

    [Fact]
    public async Task CreateAsync_requires_at_least_one_line()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                new CreatePurchaseOrderRequest
                {
                    SupplierId = supplier.Id,
                    OrderDate = OrderDate,
                    Lines = [],
                },
                CancellationToken.None));

        Assert.Contains(CreatePurchaseOrderValidator.LinesField, exception.Errors);
        await AssertNothingPersistedAsync(dbContext);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_item_lines()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(supplier.Id, (item.Id, 1m, 10m), (item.Id, 2m, 10m)),
                CancellationToken.None));

        Assert.Contains($"{CreatePurchaseOrderValidator.LinesField}[1].inventoryItemId", exception.Errors);
        await AssertNothingPersistedAsync(dbContext);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task CreateAsync_rejects_a_non_positive_quantity(decimal quantityOrdered)
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(supplier.Id, (item.Id, quantityOrdered, 10m)),
                CancellationToken.None));

        Assert.Contains($"{CreatePurchaseOrderValidator.LinesField}[0].quantityOrdered", exception.Errors);
        await AssertNothingPersistedAsync(dbContext);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_negative_unit_cost()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(supplier.Id, (item.Id, 1m, -0.01m)),
                CancellationToken.None));

        Assert.Contains($"{CreatePurchaseOrderValidator.LinesField}[0].unitCost", exception.Errors);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_zero_unit_cost()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        var response = await new CreatePurchaseOrderService(dbContext).CreateAsync(
            CreateRequest(supplier.Id, (item.Id, 3m, 0m)),
            CancellationToken.None);

        Assert.Equal(0m, response.TotalCost);
    }

    [Fact]
    public async Task CreateAsync_requires_a_supplier()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(Guid.Empty, (Guid.NewGuid(), 1m, 10m)),
                CancellationToken.None));

        Assert.Contains(CreatePurchaseOrderValidator.SupplierIdField, exception.Errors);
    }

    [Fact]
    public async Task CreateAsync_requires_an_order_date()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                new CreatePurchaseOrderRequest
                {
                    SupplierId = supplier.Id,
                    Lines = [new CreatePurchaseOrderLineRequest
                    {
                        InventoryItemId = Guid.NewGuid(),
                        QuantityOrdered = 1m,
                        UnitCost = 1m,
                    }],
                },
                CancellationToken.None));

        Assert.Contains(CreatePurchaseOrderValidator.OrderDateField, exception.Errors);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_delivery_before_the_order_date()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");

        var exception = await Assert.ThrowsAsync<CreatePurchaseOrderValidationException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                new CreatePurchaseOrderRequest
                {
                    SupplierId = supplier.Id,
                    OrderDate = OrderDate,
                    ExpectedDelivery = OrderDate.AddDays(-1),
                    Lines = [new CreatePurchaseOrderLineRequest
                    {
                        InventoryItemId = item.Id,
                        QuantityOrdered = 1m,
                        UnitCost = 1m,
                    }],
                },
                CancellationToken.None));

        Assert.Contains(CreatePurchaseOrderValidator.ExpectedDeliveryField, exception.Errors);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_supplier()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");
        var unknownSupplierId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<SupplierNotFoundException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(unknownSupplierId, (item.Id, 1m, 10m)),
                CancellationToken.None));

        Assert.Equal(unknownSupplierId, exception.SupplierId);
        await AssertNothingPersistedAsync(dbContext);
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_inventory_items()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var known = await SeedInventoryItemAsync(dbContext, supplier.Id, "Filter", "PN-1");
        var unknownItemId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PurchaseOrderItemsNotFoundException>(
            () => new CreatePurchaseOrderService(dbContext).CreateAsync(
                CreateRequest(supplier.Id, (known.Id, 1m, 10m), (unknownItemId, 1m, 10m)),
                CancellationToken.None));

        Assert.Equal([unknownItemId], exception.InventoryItemIds);
        await AssertNothingPersistedAsync(dbContext);
    }

    private static CreatePurchaseOrderRequest CreateRequest(
        Guid supplierId,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        return new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = OrderDate,
            Lines = lines
                .Select(line => new CreatePurchaseOrderLineRequest
                {
                    InventoryItemId = line.ItemId,
                    QuantityOrdered = line.Quantity,
                    UnitCost = line.UnitCost,
                })
                .ToList(),
        };
    }

    private static async Task AssertNothingPersistedAsync(HarborOpsDbContext dbContext)
    {
        Assert.Empty(await dbContext.Set<PurchaseOrder>().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.Set<PurchaseOrderLine>().ToListAsync(CancellationToken.None));
    }

    private static async Task<Supplier> SeedSupplierAsync(HarborOpsDbContext dbContext)
    {
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    private static async Task<InventoryItem> SeedInventoryItemAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        string name,
        string partNumber)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            name,
            partNumber,
            0m,
            1m,
            10m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
