using System.Data.Common;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Verifies the closure tables, their unique indexes, and the all-or-nothing commit on SQL Server.
/// </summary>
public sealed class PurchaseOrderClosurePersistenceRelationalTests : IAsyncLifetime
{
    private const int UniqueIndexViolation = 2601;
    private const string Reason = "Supplier discontinued the part";

    private static readonly Guid ActorUserId =
        Guid.Parse("6e7f8091-a2b3-4c4d-9e5f-60718293a4b5");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task A_closure_round_trips_with_its_evidence_and_leaves_stock_and_receipts_alone()
    {
        var (itemId, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 2m, ordered: 10m, received: 4.125m);
        var reason = new string('R', PurchaseOrderClosure.ReasonMaxLength);
        var request = await RequestAsync(order.Id) with { Reason = reason };

        var response = await CloseAsync(order.Id, request);

        await using var dbContext = CreateDbContext();
        var closure = await dbContext.Set<PurchaseOrderClosure>().AsNoTracking().SingleAsync();
        var line = await dbContext.Set<PurchaseOrderClosureLine>().AsNoTracking().SingleAsync();
        var orderLine = await dbContext.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync();
        var storedOrder = await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(response.Closure.ClosureId, closure.Id);
        Assert.Equal(order.Id, closure.PurchaseOrderId);
        Assert.Equal(request.ClientRequestId, closure.ClientRequestId);
        Assert.Equal(PurchaseOrderClosure.RequestHashLength, closure.RequestHash.Length);
        Assert.Equal(reason, closure.Reason);
        Assert.Equal(ActorUserId, closure.ClosedByUserId);
        Assert.Equal(DateTimeKind.Utc, response.Closure.ClosedUtc.Kind);
        Assert.Equal(response.Closure.ClosedUtc.Ticks, closure.ClosedUtc.Ticks);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, closure.OrderStatusBefore);
        Assert.Equal(PurchaseOrderStatus.ClosedShort, closure.OrderStatusAfter);
        Assert.Equal(PurchaseOrderStatus.ClosedShort, storedOrder.Status);
        Assert.Equal(order.LineId, line.PurchaseOrderLineId);
        Assert.Equal(itemId, line.InventoryItemId);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(4.125m, line.ReceivedQuantity);
        Assert.Equal(0m, line.ReturnedQuantity);
        Assert.Equal(5.875m, line.ClosedQuantity);
        Assert.Equal(10m, orderLine.QuantityOrdered);
        Assert.Equal(25m, orderLine.UnitCost);
        Assert.Equal(4.125m, orderLine.ReceivedQuantity);
        Assert.Equal(5.875m, orderLine.ClosedQuantity);
        Assert.Equal(6.125m, (await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);
        Assert.Single(await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().ToListAsync());
        Assert.Single(await dbContext.Set<InventoryAdjustment>().AsNoTracking().ToListAsync());
    }

    [RelationalFact]
    public async Task The_database_rejects_a_second_closure_with_the_same_client_request_id()
    {
        var (_, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 0m, ordered: 10m, received: 4m);
        var (_, otherOrder) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 0m, ordered: 10m, received: 4m);
        var response = await CloseAsync(order.Id, await RequestAsync(order.Id));

        await using var dbContext = CreateDbContext();
        await dbContext.Set<PurchaseOrderClosure>().AddAsync(
            await ClosedCopyAsync(dbContext, otherOrder.Id, response.Closure.ClientRequestId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task The_database_rejects_a_second_closure_of_the_same_order()
    {
        var (_, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 0m, ordered: 10m, received: 4m);
        await CloseAsync(order.Id, await RequestAsync(order.Id));

        await using var dbContext = CreateDbContext();
        await dbContext.Set<PurchaseOrderClosure>().AddAsync(await ClosedCopyAsync(dbContext, order.Id, Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task The_database_rejects_closing_one_order_line_twice()
    {
        var (_, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 0m, ordered: 10m, received: 4m);
        await CloseAsync(order.Id, await RequestAsync(order.Id));

        await using var dbContext = CreateDbContext();
        var closure = await dbContext.Set<PurchaseOrderClosure>().AsNoTracking().SingleAsync();
        var orderLine = await dbContext.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync();
        await dbContext.Set<PurchaseOrderClosureLine>().AddAsync(PurchaseOrderClosureLine.Create(closure, orderLine, 0m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task A_failed_commit_leaves_no_closure_line_quantity_or_status_behind()
    {
        var (itemId, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 3m, ordered: 10m, received: 4m);
        var request = await RequestAsync(order.Id);
        var failingCommit = new FailingCommitInterceptor();
        Guid tokenBefore;

        await using (var dbContext = CreateDbContext())
        {
            tokenBefore = (await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync()).ConcurrencyToken;
        }

        await using (var dbContext = CreateDbContext(failingCommit))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new ClosePurchaseOrderService(dbContext, TimeProvider.System)
                    .CloseAsync(order.Id, request, ActorUserId, CancellationToken.None));
        }

        Assert.True(failingCommit.Failed);

        await using (var verification = CreateDbContext())
        {
            Assert.Empty(await verification.Set<PurchaseOrderClosure>().AsNoTracking().ToListAsync());
            Assert.Empty(await verification.Set<PurchaseOrderClosureLine>().AsNoTracking().ToListAsync());
            Assert.Equal(0m, (await verification.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync()).ClosedQuantity);
            Assert.Equal(7m, (await verification.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == itemId)).Quantity);

            var stored = await verification.Set<PurchaseOrder>().AsNoTracking().SingleAsync();
            Assert.Equal(PurchaseOrderStatus.PartiallyReceived, stored.Status);
            Assert.Equal(tokenBefore, stored.ConcurrencyToken);
        }

        // Nothing changed, so the identical confirmation can simply be sent again and closes exactly once.
        var retried = await CloseAsync(order.Id, request);

        Assert.False(retried.AlreadyClosed);
        Assert.Equal(6m, retried.Closure.TotalQuantityClosed);

        await using var afterRetry = CreateDbContext();
        Assert.Single(await afterRetry.Set<PurchaseOrderClosure>().AsNoTracking().ToListAsync());
    }

    [RelationalFact]
    public async Task A_stale_purchase_order_token_is_rejected_by_the_database()
    {
        var (_, order) = await SeedPartiallyReceivedOrderAsync(quantityOnHand: 0m, ordered: 10m, received: 4m);

        await using var staleContext = CreateDbContext();
        var stale = await staleContext.Set<PurchaseOrder>().SingleAsync(candidate => candidate.Id == order.Id);
        var staleLines = await staleContext.Set<PurchaseOrderLine>()
            .Where(line => line.PurchaseOrderId == order.Id)
            .ToListAsync();

        await CloseAsync(order.Id, await RequestAsync(order.Id));

        foreach (var line in staleLines)
        {
            line.CloseOutstanding();
        }

        stale.RecordClosure(staleLines);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
    }

    private sealed record OrderSeed(Guid Id, Guid LineId);

    // A closure for another order state is built through the domain, then pointed at the order under test.
    private static async Task<PurchaseOrderClosure> ClosedCopyAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        Guid clientRequestId)
    {
        var purchaseOrder = await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync(order => order.Id == purchaseOrderId);
        var lines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToListAsync();

        if (purchaseOrder.Status != PurchaseOrderStatus.ClosedShort)
        {
            foreach (var line in lines)
            {
                line.CloseOutstanding();
            }

            purchaseOrder.RecordClosure(lines);
        }

        return PurchaseOrderClosure.Create(
            purchaseOrder,
            PurchaseOrderStatus.PartiallyReceived,
            clientRequestId,
            new string('0', PurchaseOrderClosure.RequestHashLength),
            Reason,
            ActorUserId,
            DateTime.UtcNow);
    }

    // Previews the closure and returns the request a user would send to confirm exactly that preview.
    private async Task<ClosePurchaseOrderRequest> RequestAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderClosurePreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(purchaseOrderId, CancellationToken.None);

        return new ClosePurchaseOrderRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            Reason = Reason,
        };
    }

    private async Task<ClosePurchaseOrderResponse> CloseAsync(Guid purchaseOrderId, ClosePurchaseOrderRequest request)
    {
        await using var dbContext = CreateDbContext();

        return await new ClosePurchaseOrderService(dbContext, TimeProvider.System)
            .CloseAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    // Orders one line and posts a real receipt for part of it.
    private async Task<(Guid ItemId, OrderSeed Order)> SeedPartiallyReceivedOrderAsync(
        decimal quantityOnHand,
        decimal ordered,
        decimal received)
    {
        var seed = await SeedOrderedOrderAsync(quantityOnHand, ordered);
        var receivedUtc = DateTime.UtcNow.AddHours(-1);
        var lines = new[] { new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = seed.Order.LineId, Quantity = received } };

        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, TimeProvider.System).GetPreviewAsync(
            seed.Order.Id,
            new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = receivedUtc, Lines = lines },
            CancellationToken.None);

        await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System).PostAsync(
            seed.Order.Id,
            new PostPurchaseOrderReceiptRequest
            {
                ClientRequestId = Guid.NewGuid(),
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
                ReceivedUtc = receivedUtc,
                Lines = lines,
            },
            ActorUserId,
            CancellationToken.None);

        return seed;
    }

    private async Task<(Guid ItemId, OrderSeed Order)> SeedOrderedOrderAsync(decimal quantityOnHand, decimal ordered)
    {
        await using var dbContext = CreateDbContext();

        var category = ItemCategory.Create($"Spares {Guid.NewGuid():N}", null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var supplier = Supplier.Register($"Supplier {Guid.NewGuid():N}", null, null, null, null);
        var item = InventoryItem.Create(
            category.Id,
            unit.Id,
            supplier.Id,
            "Main engine filter",
            $"PN-{Guid.NewGuid():N}",
            quantityOnHand,
            minimumQuantity: 1m,
            unitCost: 25m);
        var order = PurchaseOrder.Create(supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        var line = PurchaseOrderLine.Create(order.Id, item.Id, ordered, 25m);

        await dbContext.AddRangeAsync(category, unit, supplier, item, order, line);
        await dbContext.SaveChangesAsync();

        return (item.Id, new OrderSeed(order.Id, line.Id));
    }

    private HarborOpsDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .AddInterceptors(interceptors)
            .Options;

        return new HarborOpsDbContext(options);
    }

    // Fails the first commit after every statement of the closure has already run inside the transaction.
    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        internal bool Failed { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Failed)
            {
                Failed = true;
                throw new InvalidOperationException("Simulated failure while committing the closure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
