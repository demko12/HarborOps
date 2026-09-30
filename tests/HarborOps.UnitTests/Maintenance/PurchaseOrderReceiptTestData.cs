using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

internal static class PurchaseOrderReceiptTestData
{
    internal static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    internal static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    internal static readonly DateTime ReceivedUtc = UtcNow.AddHours(-2);
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

    internal static async Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        decimal quantity,
        string name = "Main engine filter")
    {
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            Guid.NewGuid(),
            name,
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity: 1m,
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
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        var order = PurchaseOrder.Create(supplier.Id, new DateOnly(2026, 8, 1), null);
        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.Item.Id, line.Ordered, 10m))
            .ToList();

        PurchaseOrderTestData.ApplyStatus(order, status, orderLines);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return (order, orderLines);
    }

    internal static PurchaseOrderReceiptLineEntry Entry(PurchaseOrderLine line, decimal quantity)
    {
        return new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = line.Id, Quantity = quantity };
    }

    internal static Task<PurchaseOrderReceiptPostingPreview> PreviewAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        return new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, Clock).GetPreviewAsync(
            purchaseOrderId,
            new PreviewPurchaseOrderReceiptRequest
            {
                ReceivedUtc = ReceivedUtc,
                Lines = [.. lines.Select(line => Entry(line.Line, line.Quantity))],
            },
            CancellationToken.None);
    }

    // Builds the post request the way a client confirms a preview: same lines, the preview's tokens.
    internal static PostPurchaseOrderReceiptRequest Request(
        PurchaseOrderReceiptPostingPreview preview,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        return new PostPurchaseOrderReceiptRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            ReceivedUtc = ReceivedUtc,
            Lines = [.. lines.Select(line => Entry(line.Line, line.Quantity))],
        };
    }

    internal static Task<PostPurchaseOrderReceiptResponse> PostAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        PostPurchaseOrderReceiptRequest request)
    {
        return new PostPurchaseOrderReceiptService(dbContext, Clock)
            .PostAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    // Previews and posts in one step, as a user confirming the preview straight away.
    internal static async Task<PostPurchaseOrderReceiptResponse> ReceiveAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        var preview = await PreviewAsync(dbContext, purchaseOrderId, lines);

        return await PostAsync(dbContext, purchaseOrderId, Request(preview, lines));
    }

    internal static Task<PostPurchaseOrderReceiptResponse> ReceiveRemainingAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        ReceiveRemainingPurchaseOrderRequest? request = null)
    {
        return new PostPurchaseOrderReceiptService(dbContext, Clock)
            .ReceiveRemainingAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    internal static async Task<decimal> StoredQuantityAsync(HarborOpsDbContext dbContext, Guid inventoryItemId)
    {
        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
    }

    internal static Task<int> ReceiptCountAsync(HarborOpsDbContext dbContext, Guid purchaseOrderId)
    {
        return dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .CountAsync(receipt => receipt.PurchaseOrderId == purchaseOrderId);
    }

    internal static Task<int> MovementCountAsync(HarborOpsDbContext dbContext)
    {
        return dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.PurchaseOrderReceiptId != null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
