using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class UpdateSupplierReturnServiceTests
{
    [Fact]
    public async Task UpdateAsync_changes_kept_lines_adds_new_lines_and_removes_omitted_lines()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedItemAsync(dbContext, quantity: 10m);
        var second = await SeedItemAsync(dbContext, quantity: 10m);
        var third = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Received,
            (first, 5m),
            (second, 5m),
            (third, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m), (lines[1], 1m));
        var keptLineId = await dbContext.Set<SupplierReturnLine>()
            .Where(line => line.PurchaseOrderLineId == lines[0].Id)
            .Select(line => line.Id)
            .SingleAsync();
        var token = supplierReturn.ConcurrencyToken;

        var details = await UpdateAsync(
            dbContext,
            supplierReturn.Id,
            token,
            "Updated notes",
            Entry(lines[0], 2.5m, "Corroded"),
            Entry(lines[2], 1m));

        Assert.NotEqual(token, details.ConcurrencyToken);
        Assert.Equal("Updated notes", details.Notes);
        Assert.Equal(ActorUserId, details.LastModifiedByUserId);
        Assert.Equal(UtcNow, details.LastModifiedUtc);
        Assert.Equal(
            new[] { lines[0].Id, lines[2].Id }.Order(),
            details.Lines.Select(line => line.PurchaseOrderLineId).Order());

        var kept = details.Lines.Single(line => line.PurchaseOrderLineId == lines[0].Id);

        Assert.Equal(keptLineId, kept.SupplierReturnLineId);
        Assert.Equal(2.5m, kept.Quantity);
        Assert.Equal("Corroded", kept.Reason);
        Assert.Equal(2, await dbContext.Set<SupplierReturnLine>().CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_stale_concurrency_token_and_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));

        await Assert.ThrowsAsync<SupplierReturnConcurrencyException>(() =>
            UpdateAsync(dbContext, supplierReturn.Id, Guid.NewGuid(), null, Entry(lines[0], 3m)));

        dbContext.ChangeTracker.Clear();
        Assert.Equal(1m, (await dbContext.Set<SupplierReturnLine>().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_posted_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var posted = await SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        await Assert.ThrowsAsync<SupplierReturnNotDraftException>(() =>
            UpdateAsync(dbContext, posted.Id, posted.ConcurrencyToken, null, Entry(lines[0], 2m)));
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_cancelled_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));
        supplierReturn.Cancel("Duplicate", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<SupplierReturnNotDraftException>(() =>
            UpdateAsync(dbContext, supplierReturn.Id, supplierReturn.ConcurrencyToken, null, Entry(lines[0], 2m)));
    }

    [Fact]
    public async Task UpdateAsync_excludes_its_own_quantity_but_counts_other_posted_returns()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 2m));

        var exception = await Assert.ThrowsAsync<UpdateSupplierReturnValidationException>(() =>
            UpdateAsync(dbContext, supplierReturn.Id, supplierReturn.ConcurrencyToken, null, Entry(lines[0], 3.001m)));

        var details = await UpdateAsync(
            dbContext,
            supplierReturn.Id,
            supplierReturn.ConcurrencyToken,
            null,
            Entry(lines[0], 3m));

        Assert.Contains("lines[0].quantity", exception.Errors.Keys);
        Assert.Equal(3m, Assert.Single(details.Lines).Quantity);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_line_from_another_purchase_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var (_, foreignLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));

        var exception = await Assert.ThrowsAsync<UpdateSupplierReturnValidationException>(() =>
            UpdateAsync(
                dbContext,
                supplierReturn.Id,
                supplierReturn.ConcurrencyToken,
                null,
                Entry(lines[0], 1m),
                Entry(foreignLines[0], 1m)));

        Assert.Contains("lines[1].purchaseOrderLineId", exception.Errors.Keys);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_return()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SupplierReturnNotFoundException>(() =>
            UpdateAsync(dbContext, Guid.NewGuid(), Guid.NewGuid(), null, new SupplierReturnLineEntry
            {
                PurchaseOrderLineId = Guid.NewGuid(),
                Quantity = 1m,
                Reason = Reason,
            }));
    }

    private static Task<SupplierReturnDetails> UpdateAsync(
        HarborOpsDbContext dbContext,
        Guid supplierReturnId,
        Guid expectedConcurrencyToken,
        string? notes,
        params SupplierReturnLineEntry[] lines)
    {
        return new UpdateSupplierReturnService(dbContext, Clock).UpdateAsync(
            supplierReturnId,
            new UpdateSupplierReturnRequest
            {
                ExpectedConcurrencyToken = expectedConcurrencyToken,
                Notes = notes,
                Lines = lines,
            },
            ActorUserId,
            CancellationToken.None);
    }
}
