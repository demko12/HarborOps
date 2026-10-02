using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Races closing an order short against other closes, receipts, and supplier returns on SQL Server. Every
/// round must end in a state that some serial order of the operations that succeeded could have produced:
/// nothing is over-received, and a closure never records stale quantities.
/// </summary>
public sealed class PurchaseOrderClosureConcurrencyRelationalTests : IAsyncLifetime
{
    private const int Rounds = 5;
    private const int SqlServerDeadlockErrorNumber = 1205;
    private const int SqlServerTemporalPeriodErrorNumber = 13535;
    private const string Reason = "Supplier discontinued the part";

    private static readonly Guid ActorUserId =
        Guid.Parse("5d6e7f80-9102-4a3b-8c4d-5e6f708192a3");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    private static DateTime ReceivedUtc => DateTime.UtcNow.AddHours(-1);

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
    public async Task Simultaneous_identical_close_requests_close_once_and_a_later_retry_reports_it()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m)]);
            var request = await CloseRequestAsync(order.Id);

            var outcomes = await RaceAsync(Enumerable.Range(0, 4)
                .Select(_ => (Func<Task<ClosePurchaseOrderResponse>>)(() => CloseAsync(order.Id, request)))
                .ToArray());

            // A duplicate that loses the race either reports the winner's closure or a conflict.
            AssertExpectedConflicts(outcomes.Select(outcome => outcome.Error));

            var succeeded = outcomes.Where(outcome => outcome.Error is null).Select(outcome => outcome.Result!).ToList();

            Assert.NotEmpty(succeeded);
            Assert.Single(succeeded, result => !result.AlreadyClosed);
            Assert.Single(succeeded.Select(result => result.Closure.ClosureId).Distinct());
            Assert.Equal(1, await ClosureCountAsync(order.Id));
            await AssertClosedConsistentlyAsync(order.Id);
            Assert.Equal(4m, await StoredQuantityAsync(itemId));

            var retry = await CloseAsync(order.Id, request);

            Assert.True(retry.AlreadyClosed, $"Round {round}: a retry after the race was not replayed.");
            Assert.Equal(succeeded[0].Closure.ClosureId, retry.Closure.ClosureId);
        }
    }

    [RelationalFact]
    public async Task Closes_confirmed_from_the_same_preview_with_different_client_ids_admit_exactly_one()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m)]);
            var confirmed = await CloseRequestAsync(order.Id);

            var outcomes = await RaceAsync(Enumerable.Range(0, 3)
                .Select(_ => (Func<Task<ClosePurchaseOrderResponse>>)(() =>
                    CloseAsync(order.Id, confirmed with { ClientRequestId = Guid.NewGuid() })))
                .ToArray());

            Assert.Single(outcomes, outcome => outcome.Error is null);
            AssertExpectedConflicts(outcomes.Select(outcome => outcome.Error));
            Assert.Equal(1, await ClosureCountAsync(order.Id));
            await AssertClosedConsistentlyAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task A_receipt_and_a_close_confirmed_from_the_same_state_never_both_win()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m)]);
            var receipt = await ReceiptRequestAsync(order, (0, 3m));
            var close = await CloseRequestAsync(order.Id);

            var (receiptError, closeError) = await RaceAsync(
                () => PostReceiptAsync(order.Id, receipt),
                () => CloseAsync(order.Id, close));

            AssertExpectedConflicts([receiptError, closeError]);
            Assert.True(
                receiptError is null ^ closeError is null,
                $"Round {round}: the receipt and the close must not both win or both lose.");

            if (receiptError is null)
            {
                Assert.Equal(PurchaseOrderStatus.PartiallyReceived, await StoredStatusAsync(order.Id));
                Assert.Equal(7m, await StoredQuantityAsync(itemId));
                Assert.Equal(0, await ClosureCountAsync(order.Id));
            }
            else
            {
                Assert.Equal(4m, await StoredQuantityAsync(itemId));
                await AssertClosedConsistentlyAsync(order.Id);
            }

            await AssertNeverOverReceivedAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task Receive_remaining_and_close_never_both_win()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 1m);
            var otherItemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m), (otherItemId, 2m, 0m)]);
            var close = await CloseRequestAsync(order.Id);

            var (receiptError, closeError) = await RaceAsync(
                () => ReceiveRemainingAsync(order.Id),
                () => CloseAsync(order.Id, close));

            AssertExpectedConflicts([receiptError, closeError]);
            Assert.True(receiptError is null ^ closeError is null, $"Round {round}: receipt and close disagree.");

            if (receiptError is null)
            {
                Assert.Equal(PurchaseOrderStatus.Received, await StoredStatusAsync(order.Id));
                Assert.Equal(11m, await StoredQuantityAsync(itemId));
                Assert.Equal(2m, await StoredQuantityAsync(otherItemId));
                Assert.Equal(0, await ClosureCountAsync(order.Id));
            }
            else
            {
                Assert.Equal(5m, await StoredQuantityAsync(itemId));
                Assert.Equal(0m, await StoredQuantityAsync(otherItemId));
                await AssertClosedConsistentlyAsync(order.Id);
            }

            await AssertNeverOverReceivedAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task Receipts_on_lines_sharing_an_item_and_a_close_never_close_stale_quantities()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m), (itemId, 5m, 1m)]);
            var receipt = await ReceiptRequestAsync(order, (0, 2m), (1, 4m));
            var close = await CloseRequestAsync(order.Id);

            var (receiptError, closeError) = await RaceAsync(
                () => PostReceiptAsync(order.Id, receipt),
                () => CloseAsync(order.Id, close));

            AssertExpectedConflicts([receiptError, closeError]);
            Assert.True(receiptError is null ^ closeError is null, $"Round {round}: receipt and close disagree.");
            Assert.Equal(receiptError is null ? 11m : 5m, await StoredQuantityAsync(itemId));

            if (closeError is null)
            {
                await AssertClosedConsistentlyAsync(order.Id);
            }

            await AssertNeverOverReceivedAsync(order.Id);
        }
    }

    [RelationalFact]
    public async Task A_close_and_a_supplier_return_on_the_same_order_never_lose_an_update()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var itemId = await SeedItemAsync(quantity: 0m);
            var order = await SeedReceivedOrderAsync([(itemId, 10m, 4m)]);
            var supplierReturnId = await CreateReturnAsync(order.Id, order.LineIds[0], 1m);
            var returnPreview = await PreviewReturnAsync(supplierReturnId);
            var close = await CloseRequestAsync(order.Id);

            var (closeError, returnError) = await RaceAsync(
                () => CloseAsync(order.Id, close),
                () => PostReturnAsync(returnPreview));

            AssertExpectedConflicts([closeError, returnError]);

            var returned = await PostedReturnQuantityAsync(order.LineIds[0]);

            Assert.Equal(returnError is null ? 1m : 0m, returned);
            Assert.Equal(4m - returned, await StoredQuantityAsync(itemId));
            Assert.Equal(4m, await ReceivedQuantityAsync(order.LineIds[0]));

            if (closeError is null)
            {
                // The closure's evidence reflects whether the return committed before it, never a mix.
                var evidence = await ClosureLineAsync(order.LineIds[0]);

                Assert.True(evidence.ReturnedQuantity <= returned, $"Round {round}: the closure saw a return that never posted.");
                await AssertClosedConsistentlyAsync(order.Id);
            }
        }
    }

    private sealed record OrderSeed(Guid Id, IReadOnlyList<Guid> LineIds);

    // Previews the closure and returns the request a user would send to confirm exactly that preview.
    private async Task<ClosePurchaseOrderRequest> CloseRequestAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderClosurePreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(purchaseOrderId, CancellationToken.None);

        Assert.True(preview.CanClose);

        return new ClosePurchaseOrderRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            Reason = Reason,
        };
    }

    private async Task<PostPurchaseOrderReceiptRequest> ReceiptRequestAsync(
        OrderSeed order,
        params (int LineIndex, decimal Quantity)[] lines)
    {
        var receivedUtc = ReceivedUtc;
        var entries = lines
            .Select(line => new PurchaseOrderReceiptLineEntry
            {
                PurchaseOrderLineId = order.LineIds[line.LineIndex],
                Quantity = line.Quantity,
            })
            .ToList();

        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, TimeProvider.System).GetPreviewAsync(
            order.Id,
            new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = receivedUtc, Lines = entries },
            CancellationToken.None);

        return new PostPurchaseOrderReceiptRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            ReceivedUtc = receivedUtc,
            Lines = entries,
        };
    }

    private static async Task<IReadOnlyList<(T? Result, Exception? Error)>> RaceAsync<T>(params Func<Task<T>>[] operations)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(T? Result, Exception? Error)> RunAsync(Func<Task<T>> operation)
        {
            await start.Task;

            try
            {
                return (await operation(), null);
            }
            catch (Exception exception)
            {
                return (default, exception);
            }
        }

        var tasks = operations.Select(operation => Task.Run(() => RunAsync(operation))).ToList();
        start.SetResult();

        return await Task.WhenAll(tasks);
    }

    private static async Task<(Exception? First, Exception? Second)> RaceAsync(Func<Task> first, Func<Task> second)
    {
        var outcomes = await RaceAsync<bool>(
            async () =>
            {
                await first();
                return true;
            },
            async () =>
            {
                await second();
                return true;
            });

        return (outcomes[0].Error, outcomes[1].Error);
    }

    private static void AssertExpectedConflicts(IEnumerable<Exception?> outcomes)
    {
        foreach (var exception in outcomes.OfType<Exception>())
        {
            var expected = exception
                is PurchaseOrderClosureConcurrencyException
                or PurchaseOrderClosureStalePreviewException
                or PurchaseOrderClosureBlockedException
                or PurchaseOrderReceiptConcurrencyException
                or PurchaseOrderReceiptPostingBlockedException
                or PurchaseOrderReceiptStalePreviewException
                or SupplierReturnConcurrencyException
                or SupplierReturnStalePreviewException
                or SupplierReturnPostingBlockedException
                or DbUpdateConcurrencyException
                || IsSqlConflict(exception);

            Assert.True(expected, $"Unexpected failure: {exception}");
        }
    }

    // Supplier return posting does not retry, so it may surface a deadlock or temporal clash directly.
    private static bool IsSqlConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: SqlServerDeadlockErrorNumber or SqlServerTemporalPeriodErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    // A closed order has no open outstanding, and its evidence matches the stored line quantities exactly.
    private async Task AssertClosedConsistentlyAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        var lines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToListAsync();
        var evidence = await (
            from line in dbContext.Set<PurchaseOrderClosureLine>().AsNoTracking()
            join closure in dbContext.Set<PurchaseOrderClosure>().AsNoTracking()
                on line.PurchaseOrderClosureId equals closure.Id
            where closure.PurchaseOrderId == purchaseOrderId
            select line)
            .ToDictionaryAsync(line => line.PurchaseOrderLineId);
        var status = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == purchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync();

        Assert.Equal(PurchaseOrderStatus.ClosedShort, status);
        Assert.Equal(lines.Count, evidence.Count);
        Assert.All(lines, line =>
        {
            Assert.Equal(line.QuantityOrdered, line.ReceivedQuantity + line.ClosedQuantity);
            Assert.Equal(line.ReceivedQuantity, evidence[line.Id].ReceivedQuantity);
            Assert.Equal(line.ClosedQuantity, evidence[line.Id].ClosedQuantity);
        });
    }

    private async Task AssertNeverOverReceivedAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        var lines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToListAsync();
        var receiptTotals = await (
            from line in dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking()
            join receipt in dbContext.Set<PurchaseOrderReceipt>().AsNoTracking()
                on line.PurchaseOrderReceiptId equals receipt.Id
            where receipt.PurchaseOrderId == purchaseOrderId
            group line by line.PurchaseOrderLineId into posted
            select new { PurchaseOrderLineId = posted.Key, Quantity = posted.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.PurchaseOrderLineId, row => row.Quantity);

        Assert.All(lines, line =>
        {
            Assert.True(line.ReceivedQuantity + line.ClosedQuantity <= line.QuantityOrdered);
            Assert.Equal(receiptTotals.GetValueOrDefault(line.Id), line.ReceivedQuantity);
        });
    }

    private async Task<ClosePurchaseOrderResponse> CloseAsync(Guid purchaseOrderId, ClosePurchaseOrderRequest request)
    {
        await using var dbContext = CreateDbContext();

        return await new ClosePurchaseOrderService(dbContext, TimeProvider.System)
            .CloseAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    private async Task<PostPurchaseOrderReceiptResponse> PostReceiptAsync(
        Guid purchaseOrderId,
        PostPurchaseOrderReceiptRequest request)
    {
        await using var dbContext = CreateDbContext();

        return await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .PostAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    private async Task<PostPurchaseOrderReceiptResponse> ReceiveRemainingAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .ReceiveRemainingAsync(purchaseOrderId, null, ActorUserId, CancellationToken.None);
    }

    private async Task<Guid> CreateReturnAsync(Guid purchaseOrderId, Guid purchaseOrderLineId, decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        var details = await new CreateSupplierReturnService(dbContext, TimeProvider.System).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = purchaseOrderId,
                Lines =
                [
                    new SupplierReturnLineEntry
                    {
                        PurchaseOrderLineId = purchaseOrderLineId,
                        Quantity = quantity,
                        Reason = "Damaged on arrival",
                    },
                ],
            },
            ActorUserId,
            CancellationToken.None);

        return details.SupplierReturnId;
    }

    private async Task<SupplierReturnPostingPreview> PreviewReturnAsync(Guid supplierReturnId)
    {
        await using var dbContext = CreateDbContext();

        return await new GetSupplierReturnPostingPreviewQuery(dbContext, TimeProvider.System)
            .GetPreviewAsync(supplierReturnId, CancellationToken.None);
    }

    private async Task PostReturnAsync(SupplierReturnPostingPreview preview)
    {
        await using var dbContext = CreateDbContext();

        await new PostSupplierReturnService(dbContext, TimeProvider.System).PostAsync(
            preview.SupplierReturnId,
            new PostSupplierReturnRequest
            {
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);
    }

    private async Task<Guid> SeedItemAsync(decimal quantity)
    {
        await using var dbContext = CreateDbContext();

        var category = ItemCategory.Create($"Spares {Guid.NewGuid():N}", null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var supplier = Supplier.Register($"Supplier {Guid.NewGuid():N}", null, null, null, null);
        var item = InventoryItem.Create(
            category.Id,
            unit.Id,
            supplier.Id,
            "Cooling pump seal",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity: 1m,
            unitCost: 25m);

        await dbContext.AddRangeAsync(category, unit, supplier, item);
        await dbContext.SaveChangesAsync();

        return item.Id;
    }

    // Orders the lines and posts one real receipt for the received quantities, so the order has receipt history.
    private async Task<OrderSeed> SeedReceivedOrderAsync(
        IReadOnlyList<(Guid InventoryItemId, decimal Ordered, decimal Received)> lines)
    {
        OrderSeed order;

        await using (var dbContext = CreateDbContext())
        {
            var supplierId = await dbContext.Set<InventoryItem>()
                .Where(item => item.Id == lines[0].InventoryItemId)
                .Select(item => item.SupplierId)
                .SingleAsync();
            var orderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
            var purchaseOrder = PurchaseOrder.Create(supplierId, orderDate, orderDate.AddDays(14));
            purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Ordered);
            var orderLines = lines
                .Select(line => PurchaseOrderLine.Create(purchaseOrder.Id, line.InventoryItemId, line.Ordered, 25m))
                .ToList();

            await dbContext.Set<PurchaseOrder>().AddAsync(purchaseOrder);
            await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
            await dbContext.SaveChangesAsync();

            order = new OrderSeed(purchaseOrder.Id, orderLines.Select(line => line.Id).ToList());
        }

        var received = lines
            .Select((line, index) => (LineIndex: index, Quantity: line.Received))
            .Where(line => line.Quantity > 0)
            .ToArray();

        await PostReceiptAsync(order.Id, await ReceiptRequestAsync(order, received));

        return order;
    }

    private async Task<decimal> StoredQuantityAsync(Guid inventoryItemId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
    }

    private async Task<decimal> ReceivedQuantityAsync(Guid purchaseOrderLineId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.Id == purchaseOrderLineId)
            .Select(line => line.ReceivedQuantity)
            .SingleAsync();
    }

    private async Task<PurchaseOrderStatus> StoredStatusAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(order => order.Id == purchaseOrderId)
            .Select(order => order.Status)
            .SingleAsync();
    }

    private async Task<int> ClosureCountAsync(Guid purchaseOrderId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrderClosure>()
            .AsNoTracking()
            .CountAsync(closure => closure.PurchaseOrderId == purchaseOrderId);
    }

    private async Task<PurchaseOrderClosureLine> ClosureLineAsync(Guid purchaseOrderLineId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<PurchaseOrderClosureLine>()
            .AsNoTracking()
            .SingleAsync(line => line.PurchaseOrderLineId == purchaseOrderLineId);
    }

    private async Task<decimal> PostedReturnQuantityAsync(Guid purchaseOrderLineId)
    {
        await using var dbContext = CreateDbContext();

        return await (
            from line in dbContext.Set<SupplierReturnLine>().AsNoTracking()
            join supplierReturn in dbContext.Set<SupplierReturn>().AsNoTracking()
                on line.SupplierReturnId equals supplierReturn.Id
            where line.PurchaseOrderLineId == purchaseOrderLineId
                && supplierReturn.Status == SupplierReturnStatus.Posted
            select line.Quantity)
            .SumAsync();
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
