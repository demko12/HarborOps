using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

internal static class SupplierReturnTestData
{
    internal const string Reason = "Damaged on arrival";

    internal static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    internal static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    internal static readonly Guid ActorUserId = Guid.NewGuid();

    internal static TimeProvider Clock { get; } = new FixedTimeProvider(FixedNow);

    internal static string NewDatabaseName()
    {
        return $"HarborOpsUnitTests-{Guid.NewGuid()}";
    }

    internal static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? NewDatabaseName())
            .Options;

        return new HarborOpsDbContext(options);
    }

    internal static async Task<Supplier> SeedSupplierAsync(HarborOpsDbContext dbContext, string name = "Baltic Marine Parts")
    {
        var supplier = Supplier.Register(name, null, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    internal static async Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        decimal quantity,
        string name = "Main engine filter",
        decimal minimumQuantity = 1m,
        Guid? supplierId = null)
    {
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            supplierId ?? Guid.NewGuid(),
            name,
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity,
            unitCost: 10m);

        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    internal static async Task<(PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines)> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrderStatus status,
        params (InventoryItem Item, decimal Ordered)[] lines)
    {
        var supplier = await SeedSupplierAsync(dbContext);

        return await SeedOrderAsync(dbContext, supplier, status, lines);
    }

    internal static async Task<(PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines)> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        Supplier supplier,
        PurchaseOrderStatus status,
        params (InventoryItem Item, decimal Ordered)[] lines)
    {
        var order = PurchaseOrder.Create(supplier.Id, new DateOnly(2026, 8, 1), null);

        switch (status)
        {
            case PurchaseOrderStatus.Ordered:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                break;
            case PurchaseOrderStatus.Received:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                order.ChangeStatusTo(PurchaseOrderStatus.Received);
                break;
            case PurchaseOrderStatus.Cancelled:
                order.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
                break;
        }

        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.Item.Id, line.Ordered, 10m))
            .ToList();

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return (order, orderLines);
    }

    internal static async Task<SupplierReturn> SeedReturnAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        return await SeedReturnAsync(dbContext, order, UtcNow, lines);
    }

    internal static async Task<SupplierReturn> SeedReturnAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        DateTime createdUtc,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, createdUtc);
        var returnLines = lines
            .Select(line => SupplierReturnLine.Create(supplierReturn, line.Line, line.Quantity, Reason))
            .ToList();

        await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
        await dbContext.Set<SupplierReturnLine>().AddRangeAsync(returnLines);
        await dbContext.SaveChangesAsync();

        return supplierReturn;
    }

    internal static async Task<SupplierReturn> SeedPostedReturnAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        var supplierReturn = await SeedReturnAsync(dbContext, order, lines);

        await PostAsync(dbContext, await PreviewAsync(dbContext, supplierReturn.Id));

        return await dbContext.Set<SupplierReturn>().SingleAsync(candidate => candidate.Id == supplierReturn.Id);
    }

    internal static async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(HarborOpsDbContext dbContext)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    internal static async Task<MaintenanceWorkOrder> SeedReservationAsync(
        HarborOpsDbContext dbContext,
        InventoryItem item,
        decimal quantity)
    {
        var workOrder = await SeedWorkOrderAsync(dbContext);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(
            WorkOrderPartReservation.Create(workOrder.Id, item.Id, quantity, availableQuantity: item.Quantity, UtcNow));
        item.MarkReservationsChanged();
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    internal static SupplierReturnLineEntry Entry(PurchaseOrderLine line, decimal? quantity, string? reason = Reason)
    {
        return new SupplierReturnLineEntry
        {
            PurchaseOrderLineId = line.Id,
            Quantity = quantity,
            Reason = reason,
        };
    }

    internal static Task<SupplierReturnPostingPreview> PreviewAsync(HarborOpsDbContext dbContext, Guid supplierReturnId)
    {
        return new GetSupplierReturnPostingPreviewQuery(dbContext, Clock)
            .GetPreviewAsync(supplierReturnId, CancellationToken.None);
    }

    internal static Task<PostSupplierReturnResponse> PostAsync(
        HarborOpsDbContext dbContext,
        SupplierReturnPostingPreview preview)
    {
        return new PostSupplierReturnService(dbContext, Clock).PostAsync(
            preview.SupplierReturnId,
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);
    }

    internal static async Task<decimal> StoredQuantityAsync(HarborOpsDbContext dbContext, Guid inventoryItemId)
    {
        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
    }

    internal static async Task<SupplierReturnStatus> StoredStatusAsync(HarborOpsDbContext dbContext, Guid supplierReturnId)
    {
        return await dbContext.Set<SupplierReturn>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == supplierReturnId)
            .Select(candidate => candidate.Status)
            .SingleAsync();
    }

    internal static async Task<int> MovementCountAsync(HarborOpsDbContext dbContext)
    {
        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.SupplierReturnId != null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
