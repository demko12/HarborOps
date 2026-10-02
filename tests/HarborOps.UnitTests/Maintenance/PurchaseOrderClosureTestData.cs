using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

// Closure tests build on real receipts, so the receipt test data seeds items, orders, and deliveries.
internal static class PurchaseOrderClosureTestData
{
    internal const string Reason = "Supplier discontinued the part";

    internal static readonly DateTime UtcNow = PurchaseOrderReceiptTestData.UtcNow;
    internal static readonly Guid ActorUserId = PurchaseOrderReceiptTestData.ActorUserId;

    internal static TimeProvider Clock => PurchaseOrderReceiptTestData.Clock;

    internal static string NewDatabaseName()
    {
        return PurchaseOrderReceiptTestData.NewDatabaseName();
    }

    internal static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        return PurchaseOrderReceiptTestData.CreateDbContext(databaseName);
    }

    internal static Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        decimal quantity,
        string name = "Main engine filter")
    {
        return PurchaseOrderReceiptTestData.SeedItemAsync(dbContext, quantity, name);
    }

    internal static Task<(PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines)> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrderStatus status,
        params (InventoryItem Item, decimal Ordered)[] lines)
    {
        return PurchaseOrderReceiptTestData.SeedOrderAsync(dbContext, status, lines);
    }

    // Orders the lines and posts one real receipt for the received quantities, so the order has receipt history.
    internal static async Task<(PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines)> SeedReceivedAsync(
        HarborOpsDbContext dbContext,
        params (InventoryItem Item, decimal Ordered, decimal Received)[] lines)
    {
        var (order, orderLines) = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Ordered,
            [.. lines.Select(line => (line.Item, line.Ordered))]);

        await PurchaseOrderReceiptTestData.ReceiveAsync(
            dbContext,
            order.Id,
            [.. orderLines
                .Select((line, index) => (Line: line, Quantity: lines[index].Received))
                .Where(entry => entry.Quantity > 0)]);

        return (order, orderLines);
    }

    internal static Task<PurchaseOrderClosurePreview> PreviewAsync(HarborOpsDbContext dbContext, Guid purchaseOrderId)
    {
        return new GetPurchaseOrderClosurePreviewQuery(dbContext, Clock)
            .GetPreviewAsync(purchaseOrderId, CancellationToken.None);
    }

    // Builds the close request the way a client confirms a preview: the preview's tokens and a reason.
    internal static ClosePurchaseOrderRequest Request(PurchaseOrderClosurePreview preview, string reason = Reason)
    {
        return new ClosePurchaseOrderRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            Reason = reason,
        };
    }

    internal static Task<ClosePurchaseOrderResponse> CloseAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        ClosePurchaseOrderRequest request)
    {
        return new ClosePurchaseOrderService(dbContext, Clock)
            .CloseAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    // Previews and closes in one step, as a user confirming the preview straight away.
    internal static async Task<ClosePurchaseOrderResponse> CloseRemainingAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId)
    {
        var preview = await PreviewAsync(dbContext, purchaseOrderId);

        return await CloseAsync(dbContext, purchaseOrderId, Request(preview));
    }

    internal static Task<PurchaseOrderClosureDetails> GetClosureAsync(HarborOpsDbContext dbContext, Guid purchaseOrderId)
    {
        return new GetPurchaseOrderClosureQuery(dbContext).GetClosureAsync(purchaseOrderId, CancellationToken.None);
    }

    internal static Task<decimal> StoredQuantityAsync(HarborOpsDbContext dbContext, Guid inventoryItemId)
    {
        return PurchaseOrderReceiptTestData.StoredQuantityAsync(dbContext, inventoryItemId);
    }

    internal static Task<PurchaseOrder> StoredOrderAsync(HarborOpsDbContext dbContext, Guid purchaseOrderId)
    {
        return dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleAsync(order => order.Id == purchaseOrderId);
    }

    internal static Task<Dictionary<Guid, PurchaseOrderLine>> StoredLinesAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId)
    {
        return dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToDictionaryAsync(line => line.Id);
    }

    internal static Task<int> ClosureCountAsync(HarborOpsDbContext dbContext)
    {
        return dbContext.Set<PurchaseOrderClosure>().AsNoTracking().CountAsync();
    }

    internal static Task<int> ClosureLineCountAsync(HarborOpsDbContext dbContext)
    {
        return dbContext.Set<PurchaseOrderClosureLine>().AsNoTracking().CountAsync();
    }
}
