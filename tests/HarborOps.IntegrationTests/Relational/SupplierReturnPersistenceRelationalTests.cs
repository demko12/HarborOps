using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Maintenance;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class SupplierReturnPersistenceRelationalTests : IAsyncLifetime
{
    private static readonly Guid ActorUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-9c4d-5e6f708192a3");

    private static readonly DateTime UtcNow =
        new(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);

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
    public async Task The_migration_creates_every_supplier_return_table_in_the_maintenance_schema()
    {
        await using var dbContext = CreateDbContext();

        var tables = await QueryStringsAsync(
            dbContext,
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'maintenance'");

        Assert.Contains("SupplierReturns", tables);
        Assert.Contains("SupplierReturnLines", tables);
    }

    [RelationalFact]
    public async Task Movement_history_links_to_its_source_return_and_line()
    {
        await using var dbContext = CreateDbContext();

        var columns = await QueryStringsAsync(
            dbContext,
            """
            SELECT CONCAT(COLUMN_NAME, ':', IS_NULLABLE)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'maintenance'
              AND TABLE_NAME = 'InventoryAdjustments'
            """);

        Assert.Contains("StocktakeId:YES", columns);
        Assert.Contains("SupplierReturnId:YES", columns);
        Assert.Contains("SupplierReturnLineId:YES", columns);
        Assert.Contains("QuantityBefore:NO", columns);

        // The after quantity keeps the stocktake-era column name so deployed code stays compatible.
        Assert.Contains("CountedQuantity:NO", columns);
        Assert.DoesNotContain(columns, column => column.StartsWith("QuantityAfter:", StringComparison.Ordinal));
    }

    [RelationalFact]
    public async Task Every_return_quantity_column_keeps_three_decimal_places()
    {
        await using var dbContext = CreateDbContext();

        var columns = await QueryStringsAsync(
            dbContext,
            """
            SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME, ':', NUMERIC_PRECISION, ',', NUMERIC_SCALE)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'maintenance'
              AND TABLE_NAME IN ('SupplierReturnLines', 'InventoryAdjustments')
              AND DATA_TYPE = 'decimal'
            """);

        Assert.Contains("SupplierReturnLines.Quantity:18,3", columns);
        Assert.All(columns, column => Assert.EndsWith(":18,3", column, StringComparison.Ordinal));
    }

    [RelationalFact]
    public async Task A_return_quantity_round_trips_with_three_decimal_places()
    {
        var (order, lines) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        Guid supplierReturnId;

        await using (var writeContext = CreateDbContext())
        {
            var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);
            await writeContext.Set<SupplierReturn>().AddAsync(supplierReturn);
            await writeContext.Set<SupplierReturnLine>().AddAsync(
                SupplierReturnLine.Create(supplierReturn, lines[0], 1.125m, "Damaged"));
            await writeContext.SaveChangesAsync();
            supplierReturnId = supplierReturn.Id;
        }

        await using var readContext = CreateDbContext();
        var stored = await readContext.Set<SupplierReturnLine>()
            .SingleAsync(line => line.SupplierReturnId == supplierReturnId);

        Assert.Equal(1.125m, stored.Quantity);
    }

    [RelationalFact]
    public async Task A_return_lists_a_purchase_order_line_only_once()
    {
        var (order, lines) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
            await dbContext.Set<SupplierReturnLine>().AddAsync(
                SupplierReturnLine.Create(supplierReturn, lines[0], 1m, "Damaged"));
            await dbContext.SaveChangesAsync();
        }

        await using var duplicateContext = CreateDbContext();
        await duplicateContext.Set<SupplierReturnLine>().AddAsync(
            SupplierReturnLine.Create(supplierReturn, lines[0], 2m, "Damaged again"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task A_return_line_moves_stock_at_most_once()
    {
        var (order, lines) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);
        var line = SupplierReturnLine.Create(supplierReturn, lines[0], 1m, "Damaged");

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
            await dbContext.Set<SupplierReturnLine>().AddAsync(line);
            await dbContext.Set<InventoryAdjustment>().AddAsync(
                InventoryAdjustment.CreateFromSupplierReturn(line, 10m, ActorUserId, UtcNow));
            await dbContext.SaveChangesAsync();
        }

        await using var duplicateContext = CreateDbContext();
        await duplicateContext.Set<InventoryAdjustment>().AddAsync(
            InventoryAdjustment.CreateFromSupplierReturn(line, 9m, ActorUserId, UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task Several_return_movements_for_one_item_coexist_with_stocktake_uniqueness()
    {
        var (order, lines) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m, lineCount: 2);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);
        var first = SupplierReturnLine.Create(supplierReturn, lines[0], 1m, "Damaged");
        var second = SupplierReturnLine.Create(supplierReturn, lines[1], 2m, "Wrong size");

        await using var dbContext = CreateDbContext();
        await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
        await dbContext.Set<SupplierReturnLine>().AddRangeAsync(first, second);
        await dbContext.Set<InventoryAdjustment>().AddRangeAsync(
            InventoryAdjustment.CreateFromSupplierReturn(first, 10m, ActorUserId, UtcNow),
            InventoryAdjustment.CreateFromSupplierReturn(second, 9m, ActorUserId, UtcNow));

        await dbContext.SaveChangesAsync();

        Assert.Equal(
            2,
            await dbContext.Set<InventoryAdjustment>().CountAsync(adjustment => adjustment.StocktakeId == null));
    }

    [RelationalFact]
    public async Task A_return_line_cannot_reference_an_unknown_purchase_order_line()
    {
        var (order, _) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);
        var unknownLine = PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 1m, 1m);

        await using var dbContext = CreateDbContext();
        await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
        await dbContext.Set<SupplierReturnLine>().AddAsync(
            SupplierReturnLine.Create(supplierReturn, unknownLine, 1m, "Damaged"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task A_stale_return_edit_is_rejected_by_the_concurrency_token()
    {
        var (order, _) = await SeedReceivedOrderAsync(quantityOnHand: 10m, quantityOrdered: 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Set<SupplierReturn>().AddAsync(supplierReturn);
            await dbContext.SaveChangesAsync();
        }

        await using var staleContext = CreateDbContext();
        var stale = await staleContext.Set<SupplierReturn>().SingleAsync(candidate => candidate.Id == supplierReturn.Id);

        await using (var winnerContext = CreateDbContext())
        {
            var winner = await winnerContext.Set<SupplierReturn>()
                .SingleAsync(candidate => candidate.Id == supplierReturn.Id);
            winner.ChangeNotes("Winner", ActorUserId, UtcNow);
            await winnerContext.SaveChangesAsync();
        }

        stale.Cancel("Loser", ActorUserId, UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
    }

    private async Task<(PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines)> SeedReceivedOrderAsync(
        decimal quantityOnHand,
        decimal quantityOrdered,
        int lineCount = 1)
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
        var order = PurchaseOrder.Create(supplier.Id, DateOnly.FromDateTime(UtcNow), null);
        var lines = Enumerable.Range(0, lineCount)
            .Select(_ => PurchaseOrderLine.Create(order.Id, item.Id, quantityOrdered, 25m))
            .ToList();
        PurchaseOrderSeedData.ApplyStatus(order, PurchaseOrderStatus.Received, lines);

        await dbContext.AddRangeAsync(category, unit, supplier, item, order);
        await dbContext.AddRangeAsync(lines);
        await dbContext.SaveChangesAsync();

        return (order, lines);
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        HarborOpsDbContext dbContext,
        string sql)
    {
        var results = new List<string>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await dbContext.Database.OpenConnectionAsync();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!await reader.IsDBNullAsync(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
